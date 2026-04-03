// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
namespace Microsoft.DbContextPackage.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;

    internal static class GroupHashCalculator
    {
        /// <summary>
        /// Computes a SHA256 hash for a group of entity set mappings obtained via reflection
        /// from the EF6 EntityContainerMapping object.
        /// </summary>
        /// <param name="containerMapping">The EntityContainerMapping (dynamic, obtained via reflection).</param>
        /// <param name="groupEntitySetNames">The entity set names in this group.</param>
        /// <returns>Base64-encoded SHA256 hash string.</returns>
        public static string ComputeGroupHash(dynamic containerMapping, IEnumerable<string> groupEntitySetNames)
        {
            var nameSet = new HashSet<string>(groupEntitySetNames);
            var sb = new StringBuilder();

            var entitySetMappings = (IEnumerable<dynamic>)containerMapping.EntitySetMappings;
            var orderedMappings = ((IEnumerable<dynamic>)entitySetMappings)
                .Where(m => nameSet.Contains((string)m.EntitySet.Name))
                .OrderBy(m => (string)m.EntitySet.Name);

            foreach (var esm in orderedMappings)
            {
                SerializeEntitySetMapping(sb, esm);
            }

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private static void SerializeEntitySetMapping(StringBuilder sb, dynamic esm)
        {
            string entitySetName = esm.EntitySet.Name;
            sb.Append("EntitySet:").AppendLine(entitySetName);

            foreach (var etm in esm.EntityTypeMappings)
            {
                string typeName = etm.EntityType != null ? etm.EntityType.FullName : "(null)";
                sb.Append("  TypeMapping:").AppendLine(typeName);

                foreach (var fragment in etm.Fragments)
                {
                    string storeEntitySetName = fragment.StoreEntitySet.Name;
                    sb.Append("    Fragment:").AppendLine(storeEntitySetName);

                    var propertyMappings = (IEnumerable<dynamic>)fragment.PropertyMappings;
                    foreach (var pm in propertyMappings.OrderBy(p => (string)p.Property.Name))
                    {
                        SerializePropertyMapping(sb, pm, "      ");
                    }

                    if (fragment.Conditions != null)
                    {
                        foreach (var cond in fragment.Conditions)
                        {
                            sb.Append("      Condition:");
                            if (cond.Property != null)
                            {
                                sb.Append(cond.Property.Name);
                            }

                            if (cond.Column != null)
                            {
                                sb.Append("=").Append(cond.Column.Name);
                            }

                            if (cond.Value != null)
                            {
                                sb.Append("==").Append(cond.Value.ToString());
                            }

                            if (cond.IsNull != null)
                            {
                                sb.Append(" IsNull=").Append(cond.IsNull.ToString());
                            }

                            sb.AppendLine();
                        }
                    }
                }
            }
        }

        private static void SerializePropertyMapping(StringBuilder sb, dynamic pm, string indent)
        {
            Type pmType = pm.GetType();
            string pmTypeName = pmType.Name;

            if (pmTypeName == "ScalarPropertyMapping")
            {
                sb.Append(indent).Append("Scalar:")
                    .Append(pm.Property.Name).Append("->")
                    .AppendLine((string)pm.Column.Name);
            }
            else if (pmTypeName == "ComplexPropertyMapping")
            {
                sb.Append(indent).Append("Complex:").AppendLine((string)pm.Property.Name);
                foreach (var typeMapping in pm.TypeMappings)
                {
                    foreach (var propMapping in typeMapping.PropertyMappings)
                    {
                        SerializePropertyMapping(sb, propMapping, indent + "  ");
                    }
                }
            }
            else
            {
                sb.Append(indent).Append("Property:").AppendLine((string)pm.Property.Name);
            }
        }
    }
}
