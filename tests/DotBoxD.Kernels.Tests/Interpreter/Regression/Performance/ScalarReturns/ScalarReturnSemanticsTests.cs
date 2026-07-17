using System.Globalization;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.ScalarReturns;

public sealed class ScalarReturnSemanticsTests
{
    private const long NegativeZeroBits = unchecked((long)0x8000000000000000);

    [Theory]
    [InlineData("add", 35L, 7L, 42L)]
    [InlineData("sub", 50L, 8L, 42L)]
    [InlineData("mul", 21L, 2L, 42L)]
    [InlineData("div", 84L, 2L, 42L)]
    [InlineData("rem", 86L, 44L, 42L)]
    public async Task I64_binary_operators_preserve_values_and_resources(
        string operation,
        long input,
        long right,
        long expected)
    {
        var expression = $$"""
        { "op": "{{operation}}", "left": { "var": "value" }, "right": { "i64": {{right}} } }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-i64-operator", "I64", expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(input));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((I64Value)result.Value!).Value);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 5);
    }

    [Fact]
    public async Task I64_unary_negation_preserves_value_and_resources()
    {
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression(
                "scalar-return-i64-negate",
                "I64",
                """{ "unary": "-", "operand": { "var": "value" } }"""));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(42));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(-42, ((I64Value)result.Value!).Value);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 4);
    }

    [Theory]
    [InlineData(
        """{ "op": "add", "left": { "var": "value" }, "right": { "i64": 1 } }""",
        long.MaxValue,
        "integer overflow")]
    [InlineData(
        """{ "op": "div", "left": { "var": "value" }, "right": { "i64": 0 } }""",
        1L,
        "integer division by zero")]
    public async Task I64_faults_preserve_error_and_failure_fuel(
        string expression,
        long input,
        string expectedMessage)
    {
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-i64-fault", "I64", expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(input));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(expectedMessage, result.Error.SafeMessage);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 5);
    }

    [Fact]
    public async Task I64_left_failure_prevents_right_evaluation_and_fuel()
    {
        const string expression = """
        {
          "op": "add",
          "left": { "op": "add", "left": { "var": "value" }, "right": { "i64": 1 } },
          "right": { "op": "div", "left": { "i64": 1 }, "right": { "i64": 0 } }
        }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-i64-left-first", "I64", expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(long.MaxValue));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("integer overflow", result.Error.SafeMessage);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
    }

    [Theory]
    [InlineData("add", 35.5, 6.5, 42.0)]
    [InlineData("sub", 50.5, 8.5, 42.0)]
    [InlineData("mul", 21.0, 2.0, 42.0)]
    [InlineData("div", 84.0, 2.0, 42.0)]
    [InlineData("rem", 86.5, 44.5, 42.0)]
    public async Task F64_binary_operators_preserve_values_and_resources(
        string operation,
        double input,
        double right,
        double expected)
    {
        var rightText = right.ToString(CultureInfo.InvariantCulture);
        var expression = $$"""
        { "op": "{{operation}}", "left": { "var": "value" }, "right": { "f64": {{rightText}} } }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-f64-operator", "F64", expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(input));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((F64Value)result.Value!).Value);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 5);
    }

    [Fact]
    public async Task F64_non_finite_intermediate_fails_before_outer_right_operand()
    {
        const string expression = """
        {
          "op": "add",
          "left": { "op": "mul", "left": { "var": "value" }, "right": { "f64": 2.0 } },
          "right": { "f64": 1.0 }
        }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-f64-non-finite", "F64", expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(double.MaxValue));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("f64 result must be finite", result.Error.SafeMessage);
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
    }

    [Theory]
    [InlineData(false, 4L)]
    [InlineData(true, 5L)]
    public async Task F64_return_tree_preserves_negative_zero(bool useRemainder, long expectedFuel)
    {
        var expression = useRemainder
            ? """{ "op": "rem", "left": { "var": "value" }, "right": { "f64": 3.0 } }"""
            : """{ "unary": "-", "operand": { "var": "value" } }""";
        var input = useRemainder ? -0.0 : 0.0;
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Expression("scalar-return-f64-signed-zero", "F64", expression));

        var result = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(input));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(NegativeZeroBits, BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value));
        ScalarReturnTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }
}
