using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncCommonReturnInferenceTests
{
    [Fact]
    public void Char_and_int_returns_emit_the_common_i32_wire_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static async ValueTask<int> Run(RemotePluginServer kernels)
            {
                return await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return (char)1;
                    }

                    return 2;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"I32\\\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("byte", "long", "2L", "I64", "numeric.toI64")]
    [InlineData("short", "double", "2D", "F64", "numeric.toF64")]
    public void Narrow_integral_returns_are_widened_to_the_common_wire_type(
        string narrowType,
        string methodReturnType,
        string wideLiteral,
        string wireType,
        string conversion)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static async ValueTask<{{methodReturnType}}> Run(RemotePluginServer kernels)
            {
                return await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return ({{narrowType}})1;
                    }

                    return {{wideLiteral}};
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains($"\\\"returnType\\\":\\\"{wireType}\\\"", source, StringComparison.Ordinal);
        Assert.Contains(conversion, source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return (byte)1;", "return 2;")]
    [InlineData("return 2;", "return (byte)1;")]
    public void Byte_and_int_returns_infer_int_regardless_of_order(
        string firstReturn,
        string secondReturn)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static async ValueTask<int> Run(RemotePluginServer kernels)
            {
                return await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        {{firstReturn}}
                    }

                    {{secondReturn}}
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"I32\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Incompatible_returns_remain_rejected()
    {
        var result = RunGenerator(UsageSource("""
            public static async ValueTask<object> Run(RemotePluginServer kernels)
            {
                return await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return 1;
                    }

                    return "two";
                });
            }
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "lambda must use a supported block body and capture shape",
                              StringComparison.Ordinal));
    }

    [Fact]
    public void Mixed_numeric_returns_infer_common_long_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static async ValueTask<long> Run(RemotePluginServer kernels)
            {
                return await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return 1;
                    }

                    return 2L;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"I64\\\"", source, StringComparison.Ordinal);
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }
}
