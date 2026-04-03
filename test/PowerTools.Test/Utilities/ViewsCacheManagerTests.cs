// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
namespace Microsoft.DbContextPackage.Utilities
{
    using System.Collections.Generic;
    using System.IO;
    using Xunit;

    public class ViewsCacheManagerTests
    {
        [Fact]
        public void GetCachePath_returns_sibling_json_path()
        {
            var viewsPath = @"C:\Project\MyContext.Views.cs";
            var result = ViewsCacheManager.GetCachePath(viewsPath);
            Assert.Equal(@"C:\Project\MyContext.Views.views-cache.json", result);
        }

        [Fact]
        public void Load_returns_null_when_file_does_not_exist()
        {
            var result = ViewsCacheManager.Load(@"C:\nonexistent\file.json");
            Assert.Null(result);
        }

        [Fact]
        public void Save_and_Load_roundtrip()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ViewsCacheTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            try
            {
                var cachePath = Path.Combine(tempDir, "test.views-cache.json");

                var data = new ViewsCacheData
                {
                    SchemaVersion = 1,
                    MappingHashValue = "abc123"
                };
                data.Groups.Add(new ViewsCacheGroup
                {
                    GroupHash = "hash1",
                    EntitySetNames = new List<string> { "Account", "AccountStatus" },
                    Views = new Dictionary<string, string>
                    {
                        { "Container.Account", "SELECT VALUE a FROM Accounts AS a" },
                        { "Container.AccountStatus", "SELECT VALUE s FROM AccountStatuses AS s" }
                    }
                });
                data.Groups.Add(new ViewsCacheGroup
                {
                    GroupHash = "hash2",
                    EntitySetNames = new List<string> { "Order" },
                    Views = new Dictionary<string, string>
                    {
                        { "Container.Order", "SELECT VALUE o FROM Orders AS o" }
                    }
                });

                ViewsCacheManager.Save(cachePath, data);
                Assert.True(File.Exists(cachePath));

                var loaded = ViewsCacheManager.Load(cachePath);
                Assert.NotNull(loaded);
                Assert.Equal(1, loaded.SchemaVersion);
                Assert.Equal("abc123", loaded.MappingHashValue);
                Assert.Equal(2, loaded.Groups.Count);

                Assert.Equal("hash1", loaded.Groups[0].GroupHash);
                Assert.Equal(new[] { "Account", "AccountStatus" }, loaded.Groups[0].EntitySetNames);
                Assert.Equal(2, loaded.Groups[0].Views.Count);
                Assert.Equal("SELECT VALUE a FROM Accounts AS a", loaded.Groups[0].Views["Container.Account"]);

                Assert.Equal("hash2", loaded.Groups[1].GroupHash);
                Assert.Single(loaded.Groups[1].Views);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Save_overwrites_existing_file()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ViewsCacheTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            try
            {
                var cachePath = Path.Combine(tempDir, "test.views-cache.json");

                var data1 = new ViewsCacheData { MappingHashValue = "first" };
                ViewsCacheManager.Save(cachePath, data1);

                var data2 = new ViewsCacheData { MappingHashValue = "second" };
                ViewsCacheManager.Save(cachePath, data2);

                var loaded = ViewsCacheManager.Load(cachePath);
                Assert.Equal("second", loaded.MappingHashValue);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetAllViews_merges_all_groups()
        {
            var data = new ViewsCacheData();
            data.Groups.Add(new ViewsCacheGroup
            {
                Views = new Dictionary<string, string>
                {
                    { "C.A", "view_a" },
                    { "C.B", "view_b" }
                }
            });
            data.Groups.Add(new ViewsCacheGroup
            {
                Views = new Dictionary<string, string>
                {
                    { "C.X", "view_x" }
                }
            });

            var allViews = ViewsCacheManager.GetAllViews(data);

            Assert.Equal(3, allViews.Count);
            Assert.Equal("view_a", allViews["C.A"]);
            Assert.Equal("view_b", allViews["C.B"]);
            Assert.Equal("view_x", allViews["C.X"]);
        }
    }
}
