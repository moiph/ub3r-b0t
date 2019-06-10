using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UB3RB0T.Tests
{
    [TestClass]
    public class UtilitiesTests
    {
        [TestMethod]
        public void TestAppendQueryParam()
        {
            var uri = new Uri("https://ub3rb0t.com/");
            var resultUri = uri.AppendQueryParam("foo", "bar");

            var expectedUriValue = "https://ub3rb0t.com/?foo=bar";

            Assert.AreEqual(expectedUriValue, resultUri.ToString(), "URI with no querystring did not match.");

            uri = new Uri("https://ub3rb0t.com/?foo=bar");
            resultUri = uri.AppendQueryParam("morty", "rick");

            expectedUriValue = "https://ub3rb0t.com/?foo=bar&morty=rick";

            Assert.AreEqual(expectedUriValue, resultUri.ToString(), "URI with existing query string did not match.");
        }

        [TestMethod]
        public void TestSubstringUpTo()
        {
            var text = "my string";
            var result = text.SubstringUpTo(6);

            Assert.AreEqual("my str", result);

            result = text.SubstringUpTo(99);

            Assert.AreEqual(text, result);
        }

        [TestMethod]
        public void TestReplaceMulti()
        {
            var text = "foo'bar";
            var result = text.ReplaceMulti(new[] { "'" }, "&quot;");

            Assert.AreEqual("foo&quot;bar", result);
        }

        [TestMethod]
        public void TestReplaceMultiSingle()
        {
            var text = "“foo'bar”";
            var result = text.ReplaceMulti(new[] { "'", "”", "“" }, "&quot;");

            Assert.AreEqual("&quot;foo&quot;bar&quot;", result);
        }
    }
}
