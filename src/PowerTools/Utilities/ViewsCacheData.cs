// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
namespace Microsoft.DbContextPackage.Utilities
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    internal class ViewsCacheData
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "mappingHashValue")]
        public string MappingHashValue { get; set; }

        [DataMember(Name = "groups")]
        public List<ViewsCacheGroup> Groups { get; set; }

        public ViewsCacheData()
        {
            SchemaVersion = 1;
            Groups = new List<ViewsCacheGroup>();
        }
    }

    [DataContract]
    internal class ViewsCacheGroup
    {
        [DataMember(Name = "groupHash")]
        public string GroupHash { get; set; }

        [DataMember(Name = "entitySetNames")]
        public List<string> EntitySetNames { get; set; }

        [DataMember(Name = "views")]
        public Dictionary<string, string> Views { get; set; }

        public ViewsCacheGroup()
        {
            EntitySetNames = new List<string>();
            Views = new Dictionary<string, string>();
        }
    }
}
