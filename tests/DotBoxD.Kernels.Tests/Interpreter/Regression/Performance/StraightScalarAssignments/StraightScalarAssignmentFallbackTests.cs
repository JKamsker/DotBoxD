using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

public sealed class StraightScalarAssignmentFallbackTests
{
    [Theory]
    [InlineData("I32", "i32")]
    [InlineData("I64", "i64")]
    [InlineData("F64", "f64")]
    public async Task Assigned_raw_variable_leaf_can_be_copied_to_a_new_local(
        string type,
        string literalName)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                $"straight-{literalName}-variable-leaf",
                type,
                type,
                "result",
                """{ "var": "value" }"""));
        var input = Scalar(type, 42);

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(host, plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, NumericValue(result.Value!));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 5);
    }

    [Theory]
    [InlineData("I64", "i64")]
    [InlineData("F64", "f64")]
    public async Task Unassigned_raw_variable_keeps_named_validation_error(
        string type,
        string literalName)
    {
        using var host = SandboxTestHost.Create();
        var prepared = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.ReadBeforeAssignment(
                $"straight-{literalName}-assigned-control",
                type,
                literalName,
                assignSource: true));
        var unassigned = await host.ImportJsonAsync(
            StraightScalarAssignmentModules.ReadBeforeAssignment(
                $"straight-{literalName}-read-before-assignment",
                type,
                literalName,
                assignSource: false));
        var tampered = StraightScalarAssignmentTestSupport.ReplaceModule(prepared, unassigned);

        var result = await new SandboxInterpreter().ExecuteAsync(
            tampered,
            "main",
            Scalar(type, 0),
            StraightScalarAssignmentTestSupport.Options(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("local 'source' read before assignment", result.Error.SafeMessage);
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
    }

    [Theory]
    [InlineData("I32", "i32")]
    [InlineData("I64", "i64")]
    [InlineData("F64", "f64")]
    public async Task Debug_trace_uses_generic_preorder_expression_events(
        string type,
        string literalName)
    {
        var expression = $$"""
        {
          "op": "add",
          "left": { "var": "value" },
          "right": {
            "op": "mul",
            "left": { "{{literalName}}": 2 },
            "right": { "{{literalName}}": 3 }
          }
        }
        """;
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                $"straight-{literalName}-debug-fallback",
                type,
                type,
                "value",
                expression));

        var untraced = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            Scalar(type, 4));
        Assert.True(untraced.Succeeded, untraced.Error?.SafeMessage);
        Assert.DoesNotContain(untraced.AuditEvents, audit => audit.Kind == "DebugTrace");

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            Scalar(type, 4),
            enableDebugTrace: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(10, NumericValue(result.Value!));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 9);
        var traces = result.AuditEvents.Where(audit => audit.Kind == "DebugTrace").ToArray();
        Assert.Equal(
            [
                "statement:AssignmentStatement",
                "expression:BinaryExpression",
                "expression:VariableExpression",
                "expression:BinaryExpression",
                "expression:LiteralExpression",
                "expression:LiteralExpression",
                "statement:ReturnStatement",
                "expression:VariableExpression"
            ],
            traces.Select(Node).ToArray());
        Assert.Equal(
            ["998", "997", "996", "995", "994", "993", "992", "991"],
            traces.Select(audit => audit.Fields!["fuelRemaining"]).ToArray());
    }

    [Theory]
    [InlineData("numeric.toI64", "I32", "I64", false)]
    [InlineData("numeric.toF64", "I32", "F64", false)]
    [InlineData("numeric.toF64", "I64", "F64", true)]
    public async Task Numeric_conversion_assignment_preserves_direct_values_and_resources(
        string conversion,
        string inputType,
        string returnType,
        bool inputIsI64)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                $"straight-{inputType.ToLowerInvariant()}-{returnType.ToLowerInvariant()}-conversion-direct",
                inputType,
                returnType,
                "result",
                $$"""{ "call": "{{conversion}}", "args": [{ "var": "value" }] }"""));
        var input = inputIsI64
            ? SandboxValue.FromInt64(-7)
            : SandboxValue.FromInt32(-7);

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(host, plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(-7, NumericValue(result.Value!));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
    }

    [Theory]
    [InlineData("numeric.toI64", "I32", "I64", false)]
    [InlineData("numeric.toF64", "I32", "F64", false)]
    [InlineData("numeric.toF64", "I64", "F64", true)]
    public async Task Numeric_conversion_debug_trace_retains_generic_expression_events(
        string conversion,
        string inputType,
        string returnType,
        bool inputIsI64)
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                $"straight-{inputType.ToLowerInvariant()}-{returnType.ToLowerInvariant()}-conversion-debug",
                inputType,
                returnType,
                "result",
                $$"""{ "call": "{{conversion}}", "args": [{ "var": "value" }] }"""));
        var input = inputIsI64
            ? SandboxValue.FromInt64(-7)
            : SandboxValue.FromInt32(-7);

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            input,
            enableDebugTrace: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(-7, NumericValue(result.Value!));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 6);
        Assert.Equal(
            [
                "statement:AssignmentStatement",
                "expression:CallExpression",
                "expression:VariableExpression",
                "statement:ReturnStatement",
                "expression:VariableExpression"
            ],
            result.AuditEvents
                .Where(audit => audit.Kind == "DebugTrace")
                .Select(Node)
                .ToArray());
    }

    [Fact]
    public async Task Pure_math_call_assignment_retains_binding_metering()
    {
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "straight-f64-call-fallback",
                "F64",
                "F64",
                "value",
                """{ "call": "math.floor", "args": [{ "var": "value" }] }"""));

        var result = await StraightScalarAssignmentTestSupport.ExecuteAsync(
            host,
            plan,
            SandboxValue.FromDouble(3.75));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(3, NumericValue(result.Value!));
        StraightScalarAssignmentTestSupport.AssertUsage(result.ResourceUsage, fuel: 8, hostCalls: 1);
    }

    private static SandboxValue Scalar(string type, double value)
        => type switch
        {
            "I32" => SandboxValue.FromInt32((int)value),
            "I64" => SandboxValue.FromInt64((long)value),
            "F64" => SandboxValue.FromDouble(value),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };

    private static double NumericValue(SandboxValue value)
        => value switch
        {
            I32Value number => number.Value,
            I64Value number => number.Value,
            F64Value number => number.Value,
            _ => throw new Xunit.Sdk.XunitException("unexpected straight assignment value")
        };

    private static string Node(SandboxAuditEvent audit)
        => $"{audit.Fields!["category"]}:{audit.Fields["nodeKind"]}";
}
