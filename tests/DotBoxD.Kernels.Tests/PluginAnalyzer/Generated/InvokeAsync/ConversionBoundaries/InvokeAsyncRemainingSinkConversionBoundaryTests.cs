using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncRemainingSinkConversionBoundaryTests
{
    [Theory]
    [InlineData("if (flag) { return 1; }", "if condition")]
    [InlineData("while (flag) { return 1; }", "while condition")]
    public void Conditions_reject_user_defined_bool_conversions(string statement, string sink)
    {
        var result = RunGenerator(UsageSource($$"""
            public sealed record Flag(bool Value)
            {
                public static implicit operator bool(Flag flag) => flag.Value;
            }

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var flag = new Flag(true);
                    {{statement}}
                    return 0;
                });
            """));

        AssertUnsupportedSink(result, sink);
    }

    [Theory]
    [InlineData("if (condition) { return 1; }")]
    [InlineData("while (condition) { return 1; }")]
    public void Conditions_preserve_bool_identity(string statement)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    bool condition = world.GetHealth("monster-1") > 0;
                    {{statement}}
                    return 0;
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Theory]
    [InlineData("return values[new MapKey(1)];")]
    [InlineData("if (values.ContainsKey(new MapKey(1))) { return 7; } return 0;")]
    public void Map_reads_reject_user_defined_key_conversions(string statement)
    {
        var result = RunGenerator(UsageSource($$"""
            public sealed record MapKey(int Value)
            {
                public static implicit operator long(MapKey key) => key.Value;
            }

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.Dictionary<long, int> values = new();
                    values[1L] = 7;
                    {{statement}}
                });
            """));

        AssertUnsupportedSink(result, "map key");
    }

    [Theory]
    [InlineData("return values[1];")]
    [InlineData("if (values.ContainsKey(1)) { return 7; } return 0;")]
    public void Map_reads_preserve_supported_key_widening(string statement)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.Dictionary<long, int> values = new();
                    values[1L] = 7;
                    {{statement}}
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void List_index_rejects_user_defined_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record ListIndex(int Value)
            {
                public static implicit operator int(ListIndex index) => index.Value;
            }

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> values = new();
                    values.Add(7);
                    return values[new ListIndex(0)];
                });
            """));

        AssertUnsupportedSink(result, "list index");
    }

    [Fact]
    public void List_index_preserves_int_identity()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> values = new();
                    values.Add(7);
                    return values[0];
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Foreach_iteration_variable_rejects_decimal_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<decimal> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> values = new();
                    values.Add(7);
                    foreach (decimal value in values)
                    {
                        return value;
                    }

                    return 0m;
                });
            """));

        AssertUnsupportedSink(result, "foreach");
    }

    [Fact]
    public void Foreach_iteration_variable_preserves_supported_widening()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<long> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> values = new();
                    values.Add(7);
                    foreach (long value in values)
                    {
                        return value;
                    }

                    return 0L;
                });
            """));
        var source = GeneratedSource(result);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelMethod_argument_rejects_user_defined_conversion()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record Source(int Value)
            {
                public static implicit operator int(Source source) => source.Value;
            }

            [KernelMethod]
            private static int Echo(int value) => value;

            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return Echo(new Source(1));
                });
            """));

        AssertUnsupportedSink(result, "[KernelMethod] 'Echo' parameter 'value'");
    }

    [Fact]
    public void KernelMethod_argument_preserves_supported_widening()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            [KernelMethod]
            private static long Echo(long value) => value;

            public static ValueTask<long> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return Echo(world.GetHealth("monster-1"));
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
