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
        public void TestRelativePath(string fullPath, string basePath, string expectedRelativePath)
        {
            Assert.AreEqual(expectedRelativePath, Utility.GetRelativePath(fullPath, basePath));
        }
    }
}
