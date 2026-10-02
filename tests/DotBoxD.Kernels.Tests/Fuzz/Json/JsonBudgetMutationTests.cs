using System.Globalization;
using System.Text;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

public sealed class JsonBudgetMutationTests
{
    [Theory]
    [InlineData("a", 1)]
    [InlineData("é", 2)]
    [InlineData("世", 3)]
    [InlineData("🚀", 4)]
    public void String_budget_counts_utf8_bytes_and_accepts_exact_limit(string scalar, int width)
    {
        const int limit = 65_536;
        var text = string.Concat(Enumerable.Repeat(scalar, limit / width)) + new string('a', limit % width);
        Assert.Equal(limit, Encoding.UTF8.GetByteCount(text));
        Assert.Equal(text, JsonImporter.Import(Module(text)).Id);
        AssertLimit(Module(text + "a"), "string exceeds maximum byte length");
    }

    [Fact]
    public void Document_byte_budget_accepts_exact_limit()
    {
        const int limit = 1_048_576;
        var json = Module("module");
        var exact = json + new string(' ', limit - Encoding.UTF8.GetByteCount(json));
        Assert.Equal("module", JsonImporter.Import(exact).Id);
        AssertLimit(exact + " ", "exceeds maximum byte length");
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(10000)]
    [InlineData(10001)]
    public void Array_breadth_limit_is_inclusive(int count)
    {
        var requests = string.Join(',', Enumerable.Repeat("{\"id\":\"cap\"}", count));
        var json = $$"""{"id":"wide","version":"1.0.0","functions":[],"capabilityRequests":[{{requests}}]}""";
        if (count <= 10000)
        {
            Assert.Equal(count, JsonImporter.Import(json).CapabilityRequests.Count);
        }
        else
        {
            AssertLimit(json, "array exceeds maximum breadth");
        }
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(10000)]
    [InlineData(10001)]
    public void Object_breadth_limit_is_inclusive(int count)
    {
        var entries = string.Join(',', Enumerable.Range(0, count).Select(i =>
            "\"p" + i.ToString(CultureInfo.InvariantCulture) + "\":\"v\""));
        var json = $$$"""{"id":"wide","version":"1.0.0","functions":[],"metadata":{ {{{entries}}} }}""";
        if (count <= 10000)
        {
            Assert.Equal(count, JsonImporter.Import(json).Metadata.Count);
        }
        else
        {
            AssertLimit(json, "object exceeds maximum breadth");
        }
    }

    private static string Module(string id) => $$"""{"id":"{{id}}","version":"1.0.0","functions":[]}""";

    private static void AssertLimit(string json, string message)
    {
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-LIMIT"
            && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }
}
