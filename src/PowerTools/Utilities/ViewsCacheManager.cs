// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
namespace Microsoft.DbContextPackage.Utilities
{
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Json;
    using System.Text;

    internal static class ViewsCacheManager
    {
        private static readonly DataContractJsonSerializerSettings _serializerSettings =
            new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            };

        public static string GetCachePath(string viewsPath)
        {
            DebugCheck.NotEmpty(viewsPath);

            var directory = Path.GetDirectoryName(viewsPath);
            var baseName = Path.GetFileNameWithoutExtension(viewsPath);

            return Path.Combine(directory, baseName + ".views-cache.json");
        }

        public static ViewsCacheData Load(string cachePath)
        {
            DebugCheck.NotEmpty(cachePath);

            if (!File.Exists(cachePath))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(ViewsCacheData), _serializerSettings);
            using (var stream = File.OpenRead(cachePath))
            {
                return (ViewsCacheData)serializer.ReadObject(stream);
            }
        }

        public static void Save(string cachePath, ViewsCacheData data)
        {
            DebugCheck.NotEmpty(cachePath);
            DebugCheck.NotNull(data);

            var serializer = new DataContractJsonSerializer(typeof(ViewsCacheData), _serializerSettings);
            var tempPath = cachePath + ".tmp";

            using (var stream = File.Create(tempPath))
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
                {
                    serializer.WriteObject(writer, data);
                }
            }

            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            File.Move(tempPath, cachePath);
        }

        public static Dictionary<string, string> GetAllViews(ViewsCacheData cache)
        {
            DebugCheck.NotNull(cache);

            var allViews = new Dictionary<string, string>();
            foreach (var group in cache.Groups)
            {
                foreach (var kvp in group.Views)
                {
                    allViews[kvp.Key] = kvp.Value;
                }
            }

            return allViews;
        }
    }
}
