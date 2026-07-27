using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions.GenericPrimitiveExpressionTestRuntime;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

public sealed class GenericPrimitiveExpressionProbeSuppressionTests
{
    [Fact]
    public async Task Ineligible_trace_keeps_exact_preorder_and_fuel()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64IntrinsicComparison(
                depth: 2,
                leftDeep: true,
                arithmeticOnLeft: true));

        var result = Execute(new SandboxInterpreter(), plan, Options(enableDebugTrace: true));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        AssertUsage(result.ResourceUsage, fuel: 12, hostCalls: 1);
        var traces = result.AuditEvents.Where(audit => audit.Kind == "DebugTrace").ToArray();
        Assert.Equal(
            [
                "statement:ReturnStatement",
                "expression:BinaryExpression",
                "expression:BinaryExpression",
                "expression:BinaryExpression",
                "expression:CallExpression",
                "expression:LiteralExpression",
                "binding:math.floor",
                "expression:LiteralExpression",
                "expression:LiteralExpression",
                "expression:LiteralExpression"
            ],
            traces.Select(Node));
        Assert.Equal(
            ["998", "997", "996", "995", "994", "993", "993", "990", "989", "988"],
            traces.Select(audit => audit.Fields!["fuelRemaining"]));
    }

    [Theory]
    [InlineData(false, 7L, 0)]
    [InlineData(true, 11L, 1)]
    public async Task Ineligible_fault_keeps_left_to_right_call_and_failure_order(
        bool intrinsicFirst,
        long expectedFuel,
        int expectedHostCalls)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionOrderModules.IneligibleFault(intrinsicFirst));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
            Assert.Equal("f64 result must be finite", result.Error.SafeMessage);
            AssertUsage(result.ResourceUsage, expectedFuel, expectedHostCalls);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deep_conversion_keeps_value_and_fuel(bool leftDeep)
    {
        const int depth = 96;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionOrderModules.DeepConversion(depth, leftDeep));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(result.ResourceUsage, checked((2L * depth) + 6));
        }
    }

    [Fact]
    public async Task Precancelled_ineligible_tree_stops_at_its_next_fuel_charge()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64IntrinsicComparison(
                depth: 96,
                leftDeep: true,
                arithmeticOnLeft: true));
        var function = Assert.Single(plan.Module.Functions);
        var expression = Assert.IsType<ReturnStatement>(Assert.Single(function.Body)).Value;
        var meter = new ResourceMeter(plan.Budget);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var context = new SandboxContext(
            SandboxRunId.New(),
            plan.Policy,
            meter,
            plan.Bindings,
            new InMemoryAuditSink(),
            cancellation.Token);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
        var frame = InterpreterFrame.CreateValidatedEntrypoint(
            layout,
            function,
            SandboxValue.Unit);
        var evaluator = new InterpreterEvaluator(
            plan,
            context,
            Options(),
            new FunctionFrameLayoutCache());

        Assert.ThrowsAny<OperationCanceledException>(() =>
            evaluator.Expressions.EvaluateAsync(expression, frame));
        Assert.Equal(0, meter.FuelUsed);
        Assert.Equal(0, meter.HostCalls);
    }

    private static string Node(SandboxAuditEvent audit)
        => $"{audit.Fields!["category"]}:{audit.Fields["nodeKind"]}";
}
