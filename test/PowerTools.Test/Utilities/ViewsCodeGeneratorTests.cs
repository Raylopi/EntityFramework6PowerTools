// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
namespace Microsoft.DbContextPackage.Utilities
{
    using System.Collections.Generic;
    using System.Data.Entity.Design;
    using Xunit;

    public class ViewsCodeGeneratorTests
    {
        [Fact]
        public void GenerateCode_CSharp_produces_valid_structure()
        {
            var views = new Dictionary<string, string>
            {
                { "Container.Account", "SELECT VALUE a FROM Accounts AS a" },
                { "Container.Order", "SELECT VALUE o FROM Orders AS o" }
            };

            var code = ViewsCodeGenerator.GenerateCode(
                LanguageOption.GenerateCSharpCode,
                "MyNamespace.MyContext",
                "hashABC123",
                views);

            Assert.Contains("[assembly: DbMappingViewCacheTypeAttribute(", code);
            Assert.Contains("typeof(MyNamespace.MyContext)", code);
            Assert.Contains("ViewsForBaseEntitySetshashABC123", code);
            Assert.Contains("return \"hashABC123\"", code);
            Assert.Contains("Container.Account", code);
            Assert.Contains("Container.Order", code);
            Assert.Contains("SELECT VALUE a FROM Accounts AS a", code);
            Assert.Contains("SELECT VALUE o FROM Orders AS o", code);
            Assert.Contains("GetView0()", code);
            Assert.Contains("GetView1()", code);
        }

        [Fact]
        public void GenerateCode_VB_produces_valid_structure()
        {
            var views = new Dictionary<string, string>
            {
                { "Container.Account", "SELECT VALUE a FROM Accounts AS a" }
            };

            var code = ViewsCodeGenerator.GenerateCode(
                LanguageOption.GenerateVBCode,
                "MyNamespace.MyContext",
                "hashABC123",
                views);

            Assert.Contains("<Assembly: DbMappingViewCacheTypeAttribute(", code);
            Assert.Contains("GetType(MyNamespace.MyContext)", code);
            Assert.Contains("ViewsForBaseEntitySetshashABC123", code);
            Assert.Contains("Return \"hashABC123\"", code);
            Assert.Contains("Container.Account", code);
            Assert.Contains("SELECT VALUE a FROM Accounts AS a", code);
            Assert.Contains("GetView0()", code);
        }

        [Fact]
        public void GenerateCode_CSharp_sorts_views_by_key()
        {
            var views = new Dictionary<string, string>
            {
                { "Container.Zebra", "view_z" },
                { "Container.Alpha", "view_a" },
                { "Container.Middle", "view_m" }
            };

            var code = ViewsCodeGenerator.GenerateCode(
                LanguageOption.GenerateCSharpCode,
                "Ns.Ctx",
                "hash1",
                views);

            var alphaPos = code.IndexOf("Container.Alpha");
            var middlePos = code.IndexOf("Container.Middle");
            var zebraPos = code.IndexOf("Container.Zebra");

            Assert.True(alphaPos < middlePos);
            Assert.True(middlePos < zebraPos);
        }

        [Fact]
        public void GenerateCode_CSharp_handles_empty_views()
        {
            var views = new Dictionary<string, string>();

            var code = ViewsCodeGenerator.GenerateCode(
                LanguageOption.GenerateCSharpCode,
                "Ns.Ctx",
                "hash1",
                views);

            Assert.Contains("return null;", code);
            Assert.DoesNotContain("GetView0", code);
        }
    }
}
