using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

public sealed class I64StraightAssignmentSemanticsTests
{
    [Theory]
    [InlineData("""{ "op": "add", "left": { "var": "value" }, "right": { "i64": 7 } }""", 35L, 42L, 7L)]
    [InlineData("""{ "op": "sub", "left": { "var": "value" }, "right": { "i64": 8 } }""", 50L, 42L, 7L)]
    [InlineData("""{ "op": "mul", "left": { "var": "value" }, "right": { "i64": 2 } }""", 21L, 42L, 7L)]
    [InlineData("""{ "op": "div", "left": { "var": "value" }, "right": { "i64": 2 } }""", 84L, 42L, 7L)]
    [InlineData("""{ "op": "rem", "left": { "var": "value" }, "right": { "i64": 44 } }""", 86L, 42L, 7L)]
    [InlineData("""{ "unary": "-", "operand": { "var": "value" } }""", -42L, 42L, 6L)]
    public async Task Supported_arithmetic_preserves_values_and_resources(
        string expression,
        long input,
        long expected,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-i64-supported-arithmetic",
                "I64",
                "I64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(input));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((I64Value)result.Value!).Value);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Theory]
    [InlineData("""{ "op": "add", "left": { "var": "value" }, "right": { "i64": 1 } }""", long.MaxValue, "integer overflow", 5L)]
    [InlineData("""{ "op": "sub", "left": { "var": "value" }, "right": { "i64": 1 } }""", long.MinValue, "integer overflow", 5L)]
    [InlineData("""{ "op": "mul", "left": { "var": "value" }, "right": { "i64": 2 } }""", long.MaxValue, "integer overflow", 5L)]
    [InlineData("""{ "op": "div", "left": { "var": "value" }, "right": { "i64": 0 } }""", 1L, "integer division by zero", 5L)]
    [InlineData("""{ "op": "rem", "left": { "var": "value" }, "right": { "i64": 0 } }""", 1L, "integer division by zero", 5L)]
    [InlineData("""{ "op": "div", "left": { "var": "value" }, "right": { "i64": -1 } }""", long.MinValue, "integer overflow", 5L)]
    [InlineData("""{ "op": "rem", "left": { "var": "value" }, "right": { "i64": -1 } }""", long.MinValue, "integer overflow", 5L)]
    [InlineData("""{ "unary": "-", "operand": { "var": "value" } }""", long.MinValue, "integer overflow", 4L)]
    public async Task Arithmetic_faults_preserve_diagnostics_and_failure_fuel(
        string expression,
        long input,
        string expectedMessage,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-i64-arithmetic-fault",
                "I64",
                "I64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(input));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(expectedMessage, result.Error.SafeMessage);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Fact]
    public async Task Binary_expression_reports_left_failure_before_evaluating_right()
    {
        const string expression = """
        {
          "op": "add",
          "left": { "op": "add", "left": { "var": "value" }, "right": { "i64": 1 } },
          "right": { "op": "div", "left": { "i64": 1 }, "right": { "i64": 0 } }
        }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-i64-left-first-failure",
                "I64",
                "I64",
                "value",
                expression));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromInt64(long.MaxValue));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("integer overflow", result.Error.SafeMessage);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
    }
}
