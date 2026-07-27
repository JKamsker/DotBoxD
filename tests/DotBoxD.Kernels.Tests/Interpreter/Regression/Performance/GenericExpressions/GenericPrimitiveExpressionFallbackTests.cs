using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions.GenericPrimitiveExpressionTestRuntime;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

public sealed class GenericPrimitiveExpressionFallbackTests
{
    [Theory]
    [InlineData(8, false, true)]
    [InlineData(8, true, true)]
    [InlineData(96, false, true)]
    [InlineData(96, true, true)]
    [InlineData(8, true, false)]
    public async Task F64_tree_nested_under_bool_preserves_value_and_fuel(
        int depth,
        bool leftDeep,
        bool arithmeticOnLeft)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(
                depth,
                leftDeep,
                arithmeticOnLeft));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(
                result.ResourceUsage,
                GenericPrimitiveExpressionModules.ExpectedComparisonFuel(depth));
        }
    }

    [Fact]
    public async Task Debug_trace_keeps_generic_preorder_nodes_and_fuel()
    {
        const int depth = 8;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth, leftDeep: true));

        var interpreter = new SandboxInterpreter();
        _ = Execute(interpreter, plan, Options());
        _ = Execute(interpreter, plan, Options());
        var result = Execute(interpreter, plan, Options(enableDebugTrace: true));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        AssertUsage(result.ResourceUsage, GenericPrimitiveExpressionModules.ExpectedComparisonFuel(depth));
        var traces = result.AuditEvents.Where(audit => audit.Kind == "DebugTrace").ToArray();
        var expectedNodes = new List<string>
        {
            "statement:AssignmentStatement",
            "expression:LiteralExpression",
            "statement:ReturnStatement",
            "expression:BinaryExpression"
        };
        expectedNodes.AddRange(Enumerable.Repeat("expression:BinaryExpression", depth));
        expectedNodes.AddRange(Enumerable.Repeat("expression:VariableExpression", depth + 1));
        expectedNodes.Add("expression:LiteralExpression");
        Assert.Equal(expectedNodes, traces.Select(Node));
        Assert.Equal(
            Enumerable.Range(0, expectedNodes.Count).Select(index => (998 - index).ToString()),
            traces.Select(audit => audit.Fields!["fuelRemaining"]));
    }

    [Fact]
    public async Task F64_fault_keeps_message_and_failure_fuel()
    {
        const int depth = 8;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, GenericPrimitiveExpressionModules.F64Fault(depth));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
            Assert.Equal("f64 result must be finite", result.Error.SafeMessage);
            AssertUsage(
                result.ResourceUsage,
                GenericPrimitiveExpressionModules.ExpectedFaultFuel(depth));
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Deep_ineligible_tree_preserves_value_fuel_and_single_call(
        bool leftDeep,
        bool arithmeticOnLeft)
    {
        const int depth = 96;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64IntrinsicComparison(
                depth,
                leftDeep,
                arithmeticOnLeft));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(
                result.ResourceUsage,
                GenericPrimitiveExpressionModules.ExpectedIntrinsicComparisonFuel(depth),
                hostCalls: 1);
        }
    }

    [Fact]
    public async Task I64_tree_nested_under_bool_preserves_value_and_fuel()
    {
        const int depth = 8;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.I64Comparison(depth, leftDeep: true));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(
                result.ResourceUsage,
                GenericPrimitiveExpressionModules.ExpectedComparisonFuel(depth));
        }
    }

    [Fact]
    public async Task I64_fault_keeps_message_and_failure_fuel()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, GenericPrimitiveExpressionModules.I64Fault());

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
            Assert.Equal("integer overflow", result.Error.SafeMessage);
            AssertUsage(result.ResourceUsage, fuel: 6);
        }
    }

    [Fact]
    public async Task Nested_intrinsic_call_stays_generic_and_metered_once()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, GenericPrimitiveExpressionModules.NestedIntrinsicCall());

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(4, ((F64Value)result.Value!).Value);
            AssertUsage(result.ResourceUsage, fuel: 8, hostCalls: 1);
        }
    }

    [Fact]
    public async Task Nested_numeric_conversion_stays_generic_and_metered()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, GenericPrimitiveExpressionModules.NestedNumericConversion());

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(result.ResourceUsage, fuel: 8);
        }
    }

    [Fact]
    public async Task Boxed_f64_source_stays_on_the_generic_path()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, GenericPrimitiveExpressionModules.BoxedF64Comparison());
        var function = Assert.Single(plan.Module.Functions);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);

        Assert.True(layout.IsBoxedSlot(layout.GetSlot("value")));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(
                interpreter,
                plan,
                Options(),
                SandboxValue.FromList([], SandboxType.F64));
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(result.ResourceUsage, fuel: 11);
        }
    }

    [Theory]
    [InlineData(true, 3L)]
    [InlineData(false, 4L)]
    public async Task Unknown_local_keeps_left_to_right_fault_fuel(
        bool arithmeticOnLeft,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var prepared = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 1, leftDeep: true));
        var tampered = ReplaceModule(
            prepared,
            GenericPrimitiveExpressionModules.UnknownF64Variable(arithmeticOnLeft));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, tampered, Options());
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
            Assert.Equal("unknown local 'missing' at runtime", result.Error.SafeMessage);
            AssertUsage(result.ResourceUsage, expectedFuel);
        }
    }

    private static string Node(SandboxAuditEvent audit)
        => $"{audit.Fields!["category"]}:{audit.Fields["nodeKind"]}";

}
