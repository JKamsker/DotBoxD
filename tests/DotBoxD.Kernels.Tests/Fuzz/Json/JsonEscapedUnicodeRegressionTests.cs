using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

public sealed class JsonEscapedUnicodeRegressionTests
{
    [Theory]
    [InlineData("\\uD800")]
    [InlineData("\\uDBFF")]
    [InlineData("\\uDC00")]
    [InlineData("\\uDFFF")]
    [InlineData("\\uD800a")]
    [InlineData("\\uD800\\uD800")]
    [InlineData("\\uDC00\\uD800")]
    public void Invalid_escaped_surrogates_have_diagnostics_in_values_and_property_names(string escaped)
    {
        foreach (var padding in new[] { "", new string('a', 300) })
        {
            var value = padding + escaped;
            string[] documents =
            [
                $$"""{"id":"{{value}}","version":"1.0.0","functions":[]}""",
                $$$"""{"id":"valid","version":"1.0.0","functions":[],"metadata":{"key":"{{{value}}}"}}""",
                $$$"""{"id":"valid","version":"1.0.0","functions":[],"metadata":{"{{{value}}}":"value"}}"""
            ];
            foreach (var json in documents)
            {
                var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
                Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-INVALID");
            }
        }
    }

    [Theory]
    [InlineData("\\uD800\\uDC00", "\U00010000")]
    [InlineData("\\uDBFF\\uDFFF", "\U0010FFFF")]
    [InlineData("\\uD83D\\uDE80", "🚀")]
    [InlineData("\\u0000\\n\\t\\\\\\\"", "\0\n\t\\\"")]
    public void Valid_escapes_and_surrogate_pairs_keep_their_values(string escaped, string expected)
    {
        foreach (var padding in new[] { "", new string('a', 300) })
        {
            var json = $$"""{"id":"{{padding}}{{escaped}}","version":"1.0.0","functions":[]}""";
            Assert.Equal(padding + expected, JsonImporter.Import(json).Id);
        }
    }
}
