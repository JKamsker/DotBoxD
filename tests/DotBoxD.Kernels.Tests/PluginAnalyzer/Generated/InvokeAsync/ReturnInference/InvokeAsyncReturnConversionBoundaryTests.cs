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
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
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

        AssertUnsupportedReturnConversion(result);
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

    private static void AssertUnsupportedReturnConversion(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult result)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "return expression",
                              StringComparison.OrdinalIgnoreCase));
}
