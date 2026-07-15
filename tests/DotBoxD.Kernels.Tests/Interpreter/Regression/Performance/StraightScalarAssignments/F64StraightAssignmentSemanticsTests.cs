using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

public sealed class F64StraightAssignmentSemanticsTests
{
    private const long NegativeZeroBits = unchecked((long)0x8000000000000000);

    [Theory]
    [InlineData("""{ "op": "add", "left": { "var": "value" }, "right": { "f64": 6.5 } }""", 35.5, 42.0, 7L)]
    [InlineData("""{ "op": "sub", "left": { "var": "value" }, "right": { "f64": 8.5 } }""", 50.5, 42.0, 7L)]
    [InlineData("""{ "op": "mul", "left": { "var": "value" }, "right": { "f64": 2.0 } }""", 21.0, 42.0, 7L)]
    [InlineData("""{ "op": "div", "left": { "var": "value" }, "right": { "f64": 2.0 } }""", 84.0, 42.0, 7L)]
    [InlineData("""{ "op": "rem", "left": { "var": "value" }, "right": { "f64": 44.5 } }""", 86.5, 42.0, 7L)]
    [InlineData("""{ "unary": "-", "operand": { "var": "value" } }""", -42.0, 42.0, 6L)]
    public async Task Supported_arithmetic_preserves_values_and_resources(
        string expression,
        double input,
        double expected,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-f64-supported-arithmetic",
                "F64",
                "F64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(input));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((F64Value)result.Value!).Value);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Fact]
    public async Task Intermediate_non_finite_result_fails_before_the_outer_right_operand()
    {
        const string expression = """
        {
          "op": "add",
          "left": { "op": "mul", "left": { "var": "value" }, "right": { "f64": 2.0 } },
          "right": { "f64": 1.0 }
        }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-f64-intermediate-non-finite",
                "F64",
                "F64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(double.MaxValue));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("f64 result must be finite", result.Error.SafeMessage);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
    }

    [Theory]
    [InlineData("div")]
    [InlineData("rem")]
    public async Task Division_and_remainder_by_zero_reject_non_finite_results(string operation)
    {
        var expression = $$"""
        { "op": "{{operation}}", "left": { "var": "value" }, "right": { "f64": 0.0 } }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                $"straight-f64-{operation}-by-zero",
                "F64",
                "F64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(1.0));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("f64 result must be finite", result.Error.SafeMessage);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 5);
    }

    [Theory]
    [InlineData(false, 6L)]
    [InlineData(true, 7L)]
    public async Task Unary_negation_and_remainder_preserve_negative_zero(
        bool useRemainder,
        long expectedFuel)
    {
        var expression = useRemainder
            ? """{ "op": "rem", "left": { "var": "value" }, "right": { "f64": 3.0 } }"""
            : """{ "unary": "-", "operand": { "var": "value" } }""";
        var input = useRemainder ? -0.0 : 0.0;
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-f64-signed-zero",
                "F64",
                "F64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(input));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(NegativeZeroBits, BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }
}
