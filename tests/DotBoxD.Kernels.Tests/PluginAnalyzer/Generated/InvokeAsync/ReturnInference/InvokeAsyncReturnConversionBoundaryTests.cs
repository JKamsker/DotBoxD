using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncReturnConversionBoundaryTests
{
    [Theory]
    [InlineData("return 1;", "return 2m;")]
    [InlineData("return 2m;", "return 1;")]
    public void Int_and_decimal_returns_are_rejected_regardless_of_order(
        string firstReturn,
        string secondReturn)
    {
        var result = RunGenerator(UsageSource($$"""
            public static ValueTask<decimal> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        {{firstReturn}}
                    }

                    {{secondReturn}}
                });
            """));

        AssertUnsupportedReturnConversion(result);
    }

    [Fact]
    public void Contextually_widened_narrow_constant_is_converted_before_local_storage()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<long> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    long widened = (byte)1;
                    return widened;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Equal(1, source.Split("numeric.toI64", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("long", "uint value = 1;", "2L")]
    [InlineData("double", "ulong value = 1;", "2D")]
    public void Unsupported_unsigned_return_widening_is_rejected(
        string methodReturnType,
        string declaration,
        string supportedReturn)
    {
        var result = RunGenerator(UsageSource($$"""
            public static ValueTask<{{methodReturnType}}> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    {{declaration}}
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return value;
                    }

                    return {{supportedReturn}};
                });
            """));

        AssertUnsupportedConversion(result);
    }

    [Theory]
    [InlineData("return new Source(1);", "return new Target(2L, 3);")]
    [InlineData("return new Target(2L, 3);", "return new Source(1);")]
    public void User_defined_return_conversion_is_rejected_regardless_of_order(
        string firstReturn,
        string secondReturn)
    {
        var result = RunGenerator(UsageSource($$"""
            public sealed record Source(int Value);

            public sealed record Target(long Value, int Extra)
            {
                public static implicit operator Target(Source value) => new(value.Value, 0);
            }

            public static ValueTask<Target> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        {{firstReturn}}
                    }

                    {{secondReturn}}
                });
            """));

        AssertUnsupportedReturnConversion(result);
    }

    [Theory]
    [InlineData("float value = 1F;", false)]
    [InlineData("long value = 1L;", true)]
    public void Supported_f64_return_widenings_remain_valid(
        string declaration,
        bool requiresConversionCall)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    {{declaration}}
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return value;
                    }

                    return 2D;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"F64\\\"", source, StringComparison.Ordinal);
        Assert.Equal(
            requiresConversionCall,
            source.Contains("numeric.toF64", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("decimal", "decimal converted = 1;")]
    [InlineData("Target", "Target converted = new Source(1);")]
    public void Unsupported_contextual_local_conversion_is_rejected(
        string methodReturnType,
        string declaration)
    {
        var result = RunGenerator(UsageSource($$"""
            public sealed record Source(int Value);

            public sealed record Target(long Value)
            {
                public static implicit operator Target(Source value) => new(value.Value);
            }

            public static ValueTask<{{methodReturnType}}> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    {{declaration}}
                    return converted;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "local 'converted'",
                              StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Implicit_world_var_local_uses_the_resolved_host_binding_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async world =>
                {
                    var health = world.GetHealth("monster-1");
                    return health;
                });
            """));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Unsupported_contextual_assignment_conversion_is_rejected()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<decimal> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    decimal converted = 1m;
                    converted = 1;
                    return converted;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "assignment to 'converted'",
                              StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertUnsupportedReturnConversion(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult result)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "return expression",
                              StringComparison.OrdinalIgnoreCase));

    private static void AssertUnsupportedConversion(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult result)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "numeric widening conversion",
                              StringComparison.OrdinalIgnoreCase));
}
