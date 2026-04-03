// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
namespace Microsoft.DbContextPackage.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Data.Entity.Design;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.DbContextPackage.Extensions;

    /// <summary>
    /// Orchestrates incremental view generation for EF6:
    ///   1. Loads existing cache
    ///   2. Detects changed entity-set groups via per-group hashing
    ///   3. Calls GenerateViewsIncremental() on the EF6 fork (or falls back to full generation)
    ///   4. Merges results and writes Views.cs + .views-cache.json
    /// </summary>
    internal class IncrementalViewGenerator
    {
        private readonly dynamic _mappingCollection;
        private readonly Type _mappingCollectionType;
        private readonly string _contextTypeName;
        private readonly string _baseFileName;
        private readonly LanguageOption _languageOption;

        public IncrementalViewGenerator(
            dynamic mappingCollection,
            string contextTypeName,
            string baseFileName,
            LanguageOption languageOption)
        {
            _mappingCollection = mappingCollection;
            _mappingCollectionType = (Type)((object)mappingCollection).GetType();
            _contextTypeName = contextTypeName;
            _baseFileName = baseFileName;
            _languageOption = languageOption;
        }

        /// <summary>
        /// Runs the full incremental generation flow and writes output files.
        /// </summary>
        public void Generate(string viewsPath)
        {
            DebugCheck.NotEmpty(viewsPath);

            var cachePath = ViewsCacheManager.GetCachePath(viewsPath);
            var mappingHashValue = (string)_mappingCollection.ComputeMappingHashValue();
            var existingCache = ViewsCacheManager.Load(cachePath);

            var incrementalMethod = _mappingCollectionType.GetMethod(
                "GenerateViewsIncremental",
                BindingFlags.Public | BindingFlags.Instance);

            var getCellGroupsMethod = _mappingCollectionType.GetMethod(
                "GetCellGroupEntitySetNames",
                BindingFlags.Public | BindingFlags.Instance);

            bool canDoIncremental = existingCache != null
                && existingCache.SchemaVersion == 1
                && incrementalMethod != null;

            ViewsCacheData newCache;

            if (canDoIncremental)
            {
                newCache = GenerateIncremental(
                    existingCache, incrementalMethod, mappingHashValue);
            }
            else
            {
                newCache = GenerateFull(mappingHashValue, getCellGroupsMethod);
            }

            // Write Views.cs from cache
            var allViews = ViewsCacheManager.GetAllViews(newCache);
            var code = ViewsCodeGenerator.GenerateCode(
                _languageOption, _contextTypeName, mappingHashValue, allViews);
            File.WriteAllText(viewsPath, code);

            // Write cache (atomic)
            ViewsCacheManager.Save(cachePath, newCache);
        }

        private ViewsCacheData GenerateFull(string mappingHashValue, MethodInfo getCellGroupsMethod)
        {
            // Call standard GenerateViews
            var edmSchemaErrorType = _mappingCollectionType.Assembly
                .GetType("System.Data.Entity.Core.Metadata.Edm.EdmSchemaError", true);
            var listType = typeof(List<>).MakeGenericType(edmSchemaErrorType);
            var errors = Activator.CreateInstance(listType);

            var views = _mappingCollectionType
                .GetMethod("GenerateViews", new[] { listType })
                .Invoke(_mappingCollection, new[] { errors });

            CheckForErrors(errors);

            // Build views dictionary from dynamic KeyToListMap<EntitySetBase, GeneratedView>
            var viewsDict = new Dictionary<string, string>();
            foreach (var kvp in (IEnumerable<dynamic>)views)
            {
                string extentName = kvp.Key.EntityContainer.Name + "." + kvp.Key.Name;
                string entitySql = kvp.Value.EntitySql;
                viewsDict[extentName] = entitySql;
            }

            // Build cache
            var cache = new ViewsCacheData
            {
                SchemaVersion = 1,
                MappingHashValue = mappingHashValue
            };

            // Try to get cell groups from the fork API
            IReadOnlyList<IReadOnlyList<string>> cellGroups = null;
            if (getCellGroupsMethod != null)
            {
                cellGroups = (IReadOnlyList<IReadOnlyList<string>>)getCellGroupsMethod.Invoke(
                    _mappingCollection, null);
            }

            if (cellGroups != null)
            {
                var containerMapping = GetContainerMapping();

                foreach (var group in cellGroups)
                {
                    var groupEntitySetNames = group.ToList();
                    var cacheGroup = new ViewsCacheGroup
                    {
                        EntitySetNames = groupEntitySetNames
                    };

                    // Collect views for this group
                    foreach (var esName in groupEntitySetNames)
                    {
                        // Views keys are "ContainerName.EntitySetName" — find matching
                        var matchingKey = viewsDict.Keys
                            .FirstOrDefault(k => k.EndsWith("." + esName, StringComparison.Ordinal));
                        if (matchingKey != null)
                        {
                            cacheGroup.Views[matchingKey] = viewsDict[matchingKey];
                        }
                    }

                    // Compute group hash
                    if (containerMapping != null)
                    {
                        cacheGroup.GroupHash = GroupHashCalculator.ComputeGroupHash(
                            containerMapping, groupEntitySetNames);
                    }

                    cache.Groups.Add(cacheGroup);
                }
            }
            else
            {
                // No fork API: put all views in a single group (no incremental next time)
                var singleGroup = new ViewsCacheGroup();
                foreach (var kvp in viewsDict)
                {
                    var entitySetName = kvp.Key.Contains(".")
                        ? kvp.Key.Substring(kvp.Key.LastIndexOf('.') + 1)
                        : kvp.Key;
                    singleGroup.EntitySetNames.Add(entitySetName);
                    singleGroup.Views[kvp.Key] = kvp.Value;
                }

                singleGroup.GroupHash = mappingHashValue;
                cache.Groups.Add(singleGroup);
            }

            return cache;
        }

        private ViewsCacheData GenerateIncremental(
            ViewsCacheData existingCache,
            MethodInfo incrementalMethod,
            string mappingHashValue)
        {
            var containerMapping = GetContainerMapping();
            var changedSets = new HashSet<string>();

            // Detect changed groups by recomputing groupHash
            var cachedEntitySets = new HashSet<string>();
            foreach (var group in existingCache.Groups)
            {
                foreach (var esName in group.EntitySetNames)
                {
                    cachedEntitySets.Add(esName);
                }

                if (containerMapping != null)
                {
                    var currentHash = GroupHashCalculator.ComputeGroupHash(
                        containerMapping, group.EntitySetNames);

                    if (currentHash != group.GroupHash)
                    {
                        foreach (var esName in group.EntitySetNames)
                        {
                            changedSets.Add(esName);
                        }
                    }
                }
            }

            // Detect new entity sets not in cache
            if (containerMapping != null)
            {
                var currentEntitySetNames = GetCurrentEntitySetNames(containerMapping);
                foreach (var esName in currentEntitySetNames)
                {
                    if (!cachedEntitySets.Contains(esName))
                    {
                        changedSets.Add(esName);
                    }
                }

                // Remove orphaned groups (entity sets no longer in model)
                existingCache.Groups.RemoveAll(g =>
                    g.EntitySetNames.Any(n => !currentEntitySetNames.Contains(n)));
            }

            if (changedSets.Count == 0)
            {
                // No changes - just update mappingHashValue and return
                existingCache.MappingHashValue = mappingHashValue;
                return existingCache;
            }

            // Call GenerateViewsIncremental
            var edmSchemaErrorType = _mappingCollectionType.Assembly
                .GetType("System.Data.Entity.Core.Metadata.Edm.EdmSchemaError", true);
            var errorListType = typeof(List<>).MakeGenericType(edmSchemaErrorType);
            var errors = Activator.CreateInstance(errorListType);

            var newViews = (IReadOnlyDictionary<string, string>)incrementalMethod.Invoke(
                _mappingCollection,
                new object[] { (ISet<string>)changedSets, errors });

            CheckForErrors(errors);

            // Resolve the C-side container name for building proper keys
            string containerName = null;
            if (containerMapping != null)
            {
                containerName = (string)containerMapping.EdmEntityContainer.Name;
            }

            // Merge: update existing groups that had changed sets
            foreach (var group in existingCache.Groups)
            {
                bool groupChanged = group.EntitySetNames.Any(n => changedSets.Contains(n));
                if (groupChanged)
                {
                    // Replace views with new ones
                    foreach (var esName in group.EntitySetNames)
                    {
                        // Find matching key in newViews (keys are entity set names)
                        if (newViews.ContainsKey(esName))
                        {
                            // Find the existing key in group.Views that ends with this entity set name
                            var existingKey = group.Views.Keys
                                .FirstOrDefault(k => k.EndsWith("." + esName, StringComparison.Ordinal));

                            if (existingKey != null)
                            {
                                group.Views[existingKey] = newViews[esName];
                            }
                            else
                            {
                                // New view — build proper "ContainerName.EntitySetName" key
                                var fullKey = containerName != null
                                    ? containerName + "." + esName
                                    : esName;
                                group.Views[fullKey] = newViews[esName];
                            }
                        }
                    }

                    // Recompute group hash
                    if (containerMapping != null)
                    {
                        group.GroupHash = GroupHashCalculator.ComputeGroupHash(
                            containerMapping, group.EntitySetNames);
                    }
                }
            }

            // Handle completely new entity sets that formed new groups
            var handledSets = new HashSet<string>();
            foreach (var group in existingCache.Groups)
            {
                foreach (var esName in group.EntitySetNames)
                {
                    handledSets.Add(esName);
                }
            }

            var unhandledNewSets = changedSets.Where(s => !handledSets.Contains(s)).ToList();
            if (unhandledNewSets.Count > 0)
            {
                // Group new entity sets: each new set that has a view goes into its own group
                // (EF6 internally will have grouped them properly already)
                var newGroup = new ViewsCacheGroup();
                foreach (var esName in unhandledNewSets)
                {
                    newGroup.EntitySetNames.Add(esName);
                    if (newViews.ContainsKey(esName))
                    {
                        var fullKey = containerName != null
                            ? containerName + "." + esName
                            : esName;
                        newGroup.Views[fullKey] = newViews[esName];
                    }
                }

                if (containerMapping != null)
                {
                    newGroup.GroupHash = GroupHashCalculator.ComputeGroupHash(
                        containerMapping, newGroup.EntitySetNames);
                }

                existingCache.Groups.Add(newGroup);
            }

            existingCache.MappingHashValue = mappingHashValue;
            return existingCache;
        }

        private void CheckForErrors(dynamic errors)
        {
            foreach (var error in (IEnumerable<dynamic>)errors)
            {
                if ((int)error.Severity == 1)
                {
                    throw new EdmSchemaErrorException(
                        Resources.Strings.Optimize_SchemaError(_baseFileName));
                }
            }
        }

        private dynamic GetContainerMapping()
        {
            // EntityContainerMappings is a property on StorageMappingItemCollection
            var prop = _mappingCollectionType.GetProperty(
                "EntityContainerMappings",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (prop != null)
            {
                var mappings = (IEnumerable<dynamic>)prop.GetValue(_mappingCollection);
                return mappings.FirstOrDefault();
            }

            // Try alternative: iterate the collection itself
            foreach (var item in (IEnumerable<dynamic>)_mappingCollection)
            {
                Type itemType = item.GetType();
                if (itemType.Name == "EntityContainerMapping")
                {
                    return item;
                }
            }

            return null;
        }

        private HashSet<string> GetCurrentEntitySetNames(dynamic containerMapping)
        {
            var names = new HashSet<string>();
            var entitySetMappings = (IEnumerable<dynamic>)containerMapping.EntitySetMappings;
            foreach (var esm in entitySetMappings)
            {
                names.Add((string)esm.EntitySet.Name);
            }

            return names;
        }
    }
}
