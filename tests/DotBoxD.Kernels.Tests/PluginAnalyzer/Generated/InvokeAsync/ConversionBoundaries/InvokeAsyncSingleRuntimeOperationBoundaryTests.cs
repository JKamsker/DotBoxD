using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncSingleRuntimeOperationBoundaryTests
{
    private const string FloatWorldMember = """

                [HostBinding("host.world.getScore", "game.world.score.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                float GetScore(string entityId);
        """;

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("*")]
    [InlineData("/")]
    [InlineData("%")]
    public void Runtime_single_binary_arithmetic_fails_closed(string @operator)
    {
        var result = RunGenerator(UsageSource($$"""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float left = world.GetScore("left");
                    float right = world.GetScore("right");
                    float value = left {{@operator}} right;
                    return (double)value;
                });
            """, worldMembers: FloatWorldMember));

        AssertSingleRoundingDiagnostic(result);
    }

    [Theory]
    [InlineData("+=")]
    [InlineData("-=")]
    [InlineData("*=")]
    [InlineData("/=")]
    [InlineData("%=")]
    public void Runtime_single_compound_assignment_fails_closed(string @operator)
    {
        var result = RunGenerator(UsageSource($$"""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float value = world.GetScore("left");
                    value {{@operator}} world.GetScore("right");
                    return (double)value;
                });
            """, worldMembers: FloatWorldMember));

        AssertSingleRoundingDiagnostic(result);
    }

    [Theory]
    [InlineData("value++;")]
    [InlineData("value--;")]
    [InlineData("++value;")]
    [InlineData("--value;")]
    public void Runtime_single_increment_and_decrement_fail_closed(string statement)
    {
        var result = RunGenerator(UsageSource($$"""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float value = world.GetScore("value");
                    {{statement}}
                    return (double)value;
                });
            """, worldMembers: FloatWorldMember));

        AssertSingleRoundingDiagnostic(result);
    }

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void Runtime_integral_to_single_binary_operand_conversion_fails_closed(string @operator)
    {
        var result = RunGenerator(UsageSource($$"""
            public static ValueTask<bool> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float single = world.GetScore("value");
                    int integral = world.GetHealth("monster-1");
                    return single {{@operator}} integral;
                });
            """, worldMembers: FloatWorldMember));

        AssertSingleRoundingDiagnostic(result);
    }

    [Fact]
    public void Runtime_single_derived_member_arithmetic_fails_closed()
    {
        var result = RunGenerator(UsageSource("""
            public sealed record Scores(float Left, float Right)
            {
                public float Total => Left + Right;
            }

            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var scores = new Scores(world.GetScore("left"), world.GetScore("right"));
                    return (double)scores.Total;
                });
            """, worldMembers: FloatWorldMember));

        AssertSingleRoundingDiagnostic(result);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    public void Exact_runtime_single_unary_operations_remain_supported(string @operator)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float value = world.GetScore("value");
                    float result = {{@operator}}value;
                    return (double)result;
                });
            """, worldMembers: FloatWorldMember));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Theory]
    [InlineData("==")]
    [InlineData("<")]
    public void Exact_runtime_single_comparisons_remain_supported(string @operator)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static ValueTask<bool> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float left = world.GetScore("left");
                    float right = world.GetScore("right");
                    return left {{@operator}} right;
                });
            """, worldMembers: FloatWorldMember));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Compile_time_single_arithmetic_remains_supported()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float value = 16_777_216F + 1F;
                    return (double)value;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("16777216", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_time_single_derived_member_arithmetic_is_folded_before_lowering()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed record Scores(int Seed)
            {
                public float Total => 16_777_216F + 1F;
            }

            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var scores = new Scores(world.GetHealth("monster-1"));
                    return (double)scores.Total;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("16777216", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"binary\\\":\\\"add\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_compile_time_single_conversion_remains_supported()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float value = (float)16_777_217;
                    return (double)value;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("16777216", source, StringComparison.Ordinal);
    }

    private static void AssertSingleRoundingDiagnostic(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult result)
        => Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Single", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("round", StringComparison.OrdinalIgnoreCase));
}
