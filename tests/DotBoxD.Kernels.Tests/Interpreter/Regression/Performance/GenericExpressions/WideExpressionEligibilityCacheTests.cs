using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions.GenericPrimitiveExpressionTestRuntime;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

public sealed class WideExpressionEligibilityCacheTests
{
    [Fact]
    public async Task Cached_slots_recheck_assignment_state_for_each_execution()
    {
        using var host = SandboxTestHost.Create();
        var modules = WideExpressionEligibilityCacheModules.ConditionalAssignment();
        var prepared = await PrepareAsync(host, modules.Prepared);
        var plan = ReplaceModule(prepared, modules.Unassigned);
        var interpreter = new SandboxInterpreter();

        var unassigned = Execute(
            interpreter,
            plan,
            Options(),
            SandboxValue.FromBool(false));
        var assigned = Execute(
            interpreter,
            plan,
            Options(),
            SandboxValue.FromBool(true));
        var unassignedAgain = Execute(
            interpreter,
            plan,
            Options(),
            SandboxValue.FromBool(false));

        AssertUnassigned(unassigned);
        Assert.True(assigned.Succeeded, assigned.Error?.SafeMessage);
        Assert.True(((BoolValue)assigned.Value!).Value);
        AssertUnassigned(unassignedAgain);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(65)]
    public async Task Cold_classification_handles_and_bounds_distinct_raw_slots(
        int variableCount)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            WideExpressionEligibilityCacheModules.ManyDistinctVariables(variableCount));

        var interpreter = new SandboxInterpreter();
        for (var i = 0; i < 3; i++)
        {
            var result = Execute(interpreter, plan, Options());
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(
                result.ResourceUsage,
                WideExpressionEligibilityCacheModules.ExpectedDistinctFuel(variableCount));
        }
    }

    [Fact]
    public async Task Full_cache_falls_back_for_a_fifth_expression()
    {
        const int depth = 96;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, WideExpressionEligibilityCacheModules.SaturatedCache(depth));
        var interpreter = new SandboxInterpreter();

        for (var i = 0; i < 4; i++)
        {
            for (var hit = 0; hit < 2; hit++)
            {
                var seed = Execute(
                    interpreter,
                    plan,
                    Options(),
                    entrypoint: $"seed{i}");
                Assert.True(seed.Succeeded, seed.Error?.SafeMessage);
                Assert.True(((BoolValue)seed.Value!).Value);
                AssertUsage(seed.ResourceUsage, fuel: 7);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            var overflow = Execute(
                interpreter,
                plan,
                Options(),
                entrypoint: "overflow");
            Assert.True(overflow.Succeeded, overflow.Error?.SafeMessage);
            Assert.True(((BoolValue)overflow.Value!).Value);
            AssertUsage(
                overflow.ResourceUsage,
                WideExpressionEligibilityCacheModules.ExpectedOverflowFuel(depth),
                hostCalls: 1);
        }
    }

    [Fact]
    public async Task Alternating_arithmetic_keys_both_become_cached()
    {
        const int depth = 8;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            WideExpressionEligibilityCacheModules.TwoArithmeticOperands(depth));
        var function = Assert.Single(plan.Module.Functions);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
        var frame = InterpreterFrame.CreateValidatedEntrypoint(
            layout,
            function,
            SandboxValue.Unit);
        frame.Write("left", SandboxValue.FromDouble(1));
        frame.Write("right", SandboxValue.FromDouble(2));
        var comparison = Assert.IsType<BinaryExpression>(
            Assert.IsType<ReturnStatement>(function.Body[^1]).Value);
        var cache = new FunctionFrameLayoutCache();

        Assert.False(cache.TryGetWideExpressionKind(comparison.Left, frame, out var firstKind));
        Assert.Equal(WideExpressionKind.Unsupported, firstKind);
        Assert.True(cache.TryGetWideExpressionKind(comparison.Right, frame, out var secondKind));
        Assert.Equal(WideExpressionKind.F64, secondKind);
        Assert.True(cache.TryGetWideExpressionKind(comparison.Left, frame, out firstKind));
        Assert.Equal(WideExpressionKind.F64, firstKind);
        Assert.True(cache.TryGetWideExpressionKind(comparison.Right, frame, out secondKind));
        Assert.Equal(WideExpressionKind.F64, secondKind);
    }

    [Fact]
    public async Task Shared_expression_is_classified_per_frame_layout()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            WideExpressionEligibilityCacheModules.SharedUnaryAcrossLayouts());
        var interpreter = new SandboxInterpreter();

        foreach (var entrypoint in new[] { "i64", "f64", "i64", "f64" })
        {
            var result = Execute(
                interpreter,
                plan,
                Options(),
                entrypoint: entrypoint);
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(result.ResourceUsage, fuel: 8);
        }
    }

    [Fact]
    public async Task Concurrent_cold_publication_preserves_results_and_metering()
    {
        const int depth = 8;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth, leftDeep: true));
        var interpreter = new SandboxInterpreter();
        const int executionCount = 16;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var executions = Enumerable.Range(0, executionCount)
            .Select(_ => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref started) == executionCount)
                {
                    ready.SetResult();
                }

                await start.Task;
                return Execute(interpreter, plan, Options());
            }))
            .ToArray();

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();
        var results = await Task.WhenAll(executions);

        foreach (var result in results)
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.True(((BoolValue)result.Value!).Value);
            AssertUsage(
                result.ResourceUsage,
                GenericPrimitiveExpressionModules.ExpectedComparisonFuel(depth));
        }
    }

    [Fact]
    public async Task Capacity_freezes_after_four_exact_expression_keys()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 1, leftDeep: true));
        var function = Assert.Single(plan.Module.Functions);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
        var frame = InterpreterFrame.CreateValidatedEntrypoint(
            layout,
            function,
            SandboxValue.Unit);
        var cache = new WideExpressionEligibilityCache();
        var expressions = Enumerable.Range(0, 4)
            .Select(index => LiteralArithmetic(index))
            .Append(LiteralArithmetic(0))
            .ToArray();
        Assert.Equal(expressions[0], expressions[4]);
        Assert.NotSame(expressions[0], expressions[4]);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(cache.TryGetKind(expressions[i], frame, out var kind));
            Assert.Equal(WideExpressionKind.F64, kind);
        }

        Assert.False(cache.TryGetKind(expressions[4], frame, out var overflowKind));
        Assert.Equal(WideExpressionKind.Unsupported, overflowKind);
        Assert.True(cache.TryGetKind(expressions[0], frame, out var admittedKind));
        Assert.Equal(WideExpressionKind.F64, admittedKind);
    }

    [Fact]
    public async Task Concurrent_same_key_publication_consumes_one_capacity_slot()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 1, leftDeep: true));
        var function = Assert.Single(plan.Module.Functions);
        var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
        var frame = InterpreterFrame.CreateValidatedEntrypoint(
            layout,
            function,
            SandboxValue.Unit);
        var cache = new WideExpressionEligibilityCache();
        var shared = LiteralArithmetic(0);
        const int executionCount = 16;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var publications = Enumerable.Range(0, executionCount)
            .Select(_ => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref started) == executionCount)
                {
                    ready.SetResult();
                }

                await start.Task;
                var accepted = cache.TryGetKind(shared, frame, out var kind);
                return (Accepted: accepted, Kind: kind);
            }))
            .ToArray();

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();
        var outcomes = await Task.WhenAll(publications);
        Assert.All(outcomes, outcome =>
        {
            Assert.True(outcome.Accepted);
            Assert.Equal(WideExpressionKind.F64, outcome.Kind);
        });
        for (var i = 1; i <= 3; i++)
        {
            Assert.True(cache.TryGetKind(LiteralArithmetic(i), frame, out var kind));
            Assert.Equal(WideExpressionKind.F64, kind);
        }
        Assert.False(cache.TryGetKind(LiteralArithmetic(4), frame, out var overflowKind));
        Assert.Equal(WideExpressionKind.Unsupported, overflowKind);
    }

    private static BinaryExpression LiteralArithmetic(int value)
    {
        var span = new SourceSpan(1, 1);
        return new BinaryExpression(
            new LiteralExpression(SandboxValue.FromDouble(value), span),
            "+",
            new LiteralExpression(SandboxValue.FromDouble(1), span),
            span);
    }

    private static void AssertUnassigned(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("local 'value' read before assignment", result.Error.SafeMessage);
    }
}
