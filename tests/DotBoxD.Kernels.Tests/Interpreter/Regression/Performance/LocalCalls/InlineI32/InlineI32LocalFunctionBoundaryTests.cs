using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls.InlineI32;

public sealed class InlineI32LocalFunctionBoundaryTests
{
    private const string Increment =
        """{ "op": "add", "left": { "var": "operand" }, "right": { "i32": 1 } }""";

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(4L)]
    [InlineData(5L)]
    [InlineData(6L)]
    [InlineData(7L)]
    [InlineData(8L)]
    [InlineData(9L)]
    [InlineData(10L)]
    public async Task Every_fuel_boundary_fails_on_the_same_next_node(long maxFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall("inline-i32-fuel-boundary", Increment),
            maxFuel);

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input: 41);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal("fuel exhausted", result.Error.SafeMessage);
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, fuel: maxFuel + 1);
    }

    [Fact]
    public async Task Exact_fuel_budget_succeeds_without_overcharging()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall("inline-i32-exact-fuel", Increment),
            maxFuel: 11);

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input: 41);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(42, InlineI32LocalFunctionTestSupport.ReadInt32(result));
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, fuel: 11);
    }

    [Theory]
    [InlineData(
        """{ "op": "div", "left": { "var": "operand" }, "right": { "i32": 0 } }""",
        1,
        "integer division by zero",
        9L)]
    [InlineData(
        """{ "op": "add", "left": { "op": "add", "left": { "var": "operand" }, "right": { "i32": 2147483647 } }, "right": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } } }""",
        1,
        "integer overflow",
        10L)]
    public async Task Arithmetic_faults_preserve_incremental_fuel_and_left_first_order(
        string expression,
        int input,
        string expectedError,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall("inline-i32-fault-order", expression));

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(expectedError, result.Error.SafeMessage);
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Theory]
    [InlineData(1, false, 4L)]
    [InlineData(2, true, 11L)]
    public async Task Helper_call_honors_exact_call_depth_boundary(
        int maxCallDepth,
        bool expectedSuccess,
        long expectedFuel)
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall("inline-i32-call-depth", Increment),
            maxCallDepth: maxCallDepth);

        var result = await InlineI32LocalFunctionTestSupport.ExecuteAsync(plan, input: 41);

        Assert.Equal(expectedSuccess, result.Succeeded);
        Assert.Equal(expectedSuccess ? null : SandboxErrorCode.QuotaExceeded, result.Error?.Code);
        Assert.Equal(expectedSuccess ? null : "call depth exceeded", result.Error?.SafeMessage);
        InlineI32LocalFunctionTestSupport.AssertUsage(result.ResourceUsage, expectedFuel);
    }

    [Fact]
    public async Task Inline_helper_checks_cancellation_before_its_first_fuel_charge()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.SingleCall("inline-i32-cancellation", Increment));
        var function = plan.Module.Functions.Single(candidate => candidate.Id == "main");
        var assignment = Assert.IsType<AssignmentStatement>(function.Body[0]);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
        var frame = InterpreterFrame.CreateValidatedEntrypoint(
            layout,
            function,
            SandboxValue.FromInt32(41));
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
        var evaluator = new InterpreterEvaluator(
            plan,
            context,
            InlineI32LocalFunctionTestSupport.Options(),
            new FunctionFrameLayoutCache());

        Assert.Throws<OperationCanceledException>(() =>
            evaluator.Expressions.TryEvaluateInlineInt32Call(
                assignment.Value,
                frame,
                out _,
                out _));
        Assert.Equal(0, meter.FuelUsed);
    }

    [Fact]
    public async Task Bounded_plan_cache_fails_closed_for_an_overflow_call_site()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.Recurrences(
                "inline-i32-cache-overflow",
                count: 17,
                inlineable: true));
        var function = plan.Module.Functions.Single(candidate => candidate.Id == "main");
        var calls = function.Body
            .OfType<AssignmentStatement>()
            .Select(assignment => Assert.IsType<CallExpression>(assignment.Value))
            .ToArray();
        var meter = new ResourceMeter(plan.Budget);
        using var context = new SandboxContext(
            SandboxRunId.New(),
            plan.Policy,
            meter,
            plan.Bindings,
            new InMemoryAuditSink(),
            CancellationToken.None);
        var evaluator = new InterpreterEvaluator(
            plan,
            context,
            InlineI32LocalFunctionTestSupport.Options(),
            new FunctionFrameLayoutCache());

        foreach (var call in calls)
        {
            Assert.True(evaluator.TryGetInlineI32LocalFunctionCallPlan(call, out _, out _));
        }

        Assert.False(evaluator.TryGetInlineI32LocalFunctionCallPlan(calls[0], out _, out _));
        Assert.Equal(0, meter.FuelUsed);
    }
}
