using System.Text;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

public sealed class JsonAggregateBudgetMutationTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Aggregate_string_budget_includes_keys_and_values(int delta)
    {
        // Root keys = 26 bytes, root values = 6, metadata keys = 8.
        const int overhead = 40;
        var values = Enumerable.Repeat(new string('a', 65_536), 7)
            .Append(new string('b', 524_288 - overhead - 7 * 65_536 + delta)).ToArray();
        var entries = string.Join(',', values.Select((value, index) => $"\"{(char)('a' + index)}\":\"{value}\""));
        var json = $$$"""{"id":"m","version":"1.0.0","functions":[],"metadata":{ {{{entries}}} }}""";
        if (delta <= 0)
        {
            Assert.Equal(8, JsonImporter.Import(json).Metadata.Count);
        }
        else
        {
            AssertLimit(json, "maximum total string byte length");
        }
    }

    [Theory]
    [InlineData(57)]
    [InlineData(58)]
    [InlineData(59)]
    public void Nesting_depth_limit_is_inclusive(int unaryCount)
    {
        var expression = "{\"i32\":1}";
        for (var i = 0; i < unaryCount; i++)
        {
            expression = "{\"unary\":\"-\",\"operand\":" + expression + "}";
        }

        var json = $$$"""{"id":"deep","version":"1.0.0","functions":[{"id":"main","returnType":"I32","body":[{"op":"return","value":{{{expression}}}}]}]}""";
        if (unaryCount <= 58)
        {
            Assert.Single(JsonImporter.Import(json).Functions);
        }
        else
        {
            AssertLimit(json, "maximum depth");
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Token_budget_counts_container_end_tokens(int delta)
    {
        // Outer array (2) + ten inner arrays (20) + 99,978 scalar tokens = 100,000.
        // Ten arrays keep every individual container below its 10,000-item limit.
        var builder = new StringBuilder("[");
        for (var group = 0; group < 10; group++)
        {
            if (group > 0)
            { builder.Append(','); }
            builder.Append('[');
            var count = group < 9 ? 9998 : 9996 + delta;
            builder.AppendJoin(',', Enumerable.Repeat("0", count));
            builder.Append(']');
        }

        builder.Append(']');
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(builder.ToString()));
        if (delta <= 0)
        {
            // Budget scan succeeds; the root array is rejected later by the module schema.
            Assert.DoesNotContain(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-LIMIT");
            Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-TYPE");
        }
        else
        {
            Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-LIMIT"
                && diagnostic.Message.Contains("maximum token count", StringComparison.Ordinal));
        }
    }

    private static void AssertLimit(string json, string message)
    {
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-LIMIT"
            && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }
}
