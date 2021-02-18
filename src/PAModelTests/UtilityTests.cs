// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.PowerPlatform.Formulas.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PAModelTests
{
    [TestClass]
    public class UtilityTests
    {
        [DataTestMethod]
        [DataRow("\r\t!$/^%", "%0d%09%21%24%2f%5e%25")]
        [DataRow("\u4523", "%%4523")]
        public void TestEscaping(string unescaped, string escaped)
        {
            Assert.AreEqual(Utility.EscapeFilename(unescaped), escaped);
            Assert.AreEqual(Utility.UnEscapeFilename(escaped), unescaped);
        }

        [DataTestMethod]
        [DataRow("foo-%41", "foo-A")]
        [DataRow("[]_' ", "[]_' ")] // unescape only touches % character.
        public void TestUnescape(string escaped, string unescaped)
        {
            Assert.AreEqual(Utility.UnEscapeFilename(escaped), unescaped);
        }

        [TestMethod]
        public void TestNotEscaped()
        {
            // Not escaped.
            var a = "0123456789AZaz[]_. \\";
            Assert.AreEqual(Utility.EscapeFilename(a), a);
        }

        [DataTestMethod]
        [DataRow("C:\\Foo\\Bar\\Baz", "C:\\Foo", "Bar\\Baz\\")]
        [DataRow("C:\\Foo\\Bar\\Baz", "C:\\Foo\\", "Bar\\Baz\\")]
        [DataRow("C:\\Foo\\Bar\\Baz\\", "C:\\Foo\\", "Bar\\Baz\\")]
        [DataRow("C:\\Foo\\Bar.msapp", "C:\\Foo", "Bar.msapp")]
        [DataRow("C:\\Foo\\Bar.msapp", "C:\\Foo\\", "Bar.msapp")]
        [DataRow("C:\\Foo\\Bar.msapp", "C:\\", "Foo\\Bar.msapp")]
        [DataRow(@"C:\DataSources\JourneyPlanner|Sendforapproval.json", "C:\\", @"DataSources\JourneyPlanner|Sendforapproval.json")]
        [DataRow(@"C:\DataSources\JourneyPlanner%7cSendforapproval.json", "C:\\", @"DataSources\JourneyPlanner%7cSendforapproval.json")]
        [DataRow(@"d:\app\Src\EditorState\Screen%252.editorstate.json",
            @"d:\app", @"Src\EditorState\Screen%252.editorstate.json" )]
        public void TestRelativePath(string fullPath, string basePath, string expectedRelativePath)
        {
            Assert.AreEqual(expectedRelativePath, Utility.GetRelativePath(fullPath, basePath));
        }


        // Verify regression from
        // https://github.com/microsoft/PowerApps-Language-Tooling/issues/153 
        [TestMethod]
        public void Regression153()
        {
            // Not escaped.
            var path = @"DataSources\JourneyPlanner|Sendforapproval.json";
            var escape = Utility.EscapeFilename(path);

            var original = Utility.UnEscapeFilename(escape);

            Assert.AreEqual(path, original);
        }
    }
}
