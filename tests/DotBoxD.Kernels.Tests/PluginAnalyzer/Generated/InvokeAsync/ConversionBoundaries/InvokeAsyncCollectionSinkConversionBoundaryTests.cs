using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncCollectionSinkConversionBoundaryTests
{
    [Fact]
    public void Map_value_rejects_unsupported_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.Dictionary<string, decimal> values = new();
                    values["health"] = 1;
                    return world.GetHealth("monster-1");
                });
            """));

        AssertUnsupportedSink(result, "map value");
    }

    [Fact]
    public void Map_key_rejects_user_defined_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record Source(int Value)
            {
                public static implicit operator long(Source source) => source.Value;
            }

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.Dictionary<long, int> values = new();
                    values[new Source(1)] = 2;
                    return world.GetHealth("monster-1");
                });
            """));

        AssertUnsupportedSink(result, "map key");
    }

    [Fact]
    public void Map_key_and_value_preserve_supported_widening_conversions()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.Dictionary<long, long> values = new();
                    values[1] = 2;
                    return world.GetHealth("monster-1");
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.True(source.Split("numeric.toI64", StringSplitOptions.None).Length - 1 >= 2, source);
    }

    [Fact]
    public void List_item_rejects_unsupported_contextual_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<decimal> values = new();
                    values.Add(1);
                    return world.GetHealth("monster-1");
                });
            """));

        AssertUnsupportedSink(result, "list item");
    }

    [Fact]
    public void List_item_preserves_supported_widening_conversion()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<long> values = new();
                    values.Add(1);
                    return world.GetHealth("monster-1");
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    private static string GeneratedSource(Microsoft.CodeAnalysis.GeneratorDriverRunResult result)
        => string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

    private static void AssertUnsupportedSink(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult result,
        string sink)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(sink, StringComparison.OrdinalIgnoreCase));
}
