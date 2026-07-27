using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

public sealed class MultiAssignmentLoopFallbackTraceConcurrencyTests
{
    public static TheoryData<string> FallbackModules
        => new()
        {
            MultiAssignmentFallbackModules.ForRange,
            MultiAssignmentFallbackModules.While
        };

    public static TheoryData<string, string, int> TraceModules
        => new()
        {
            { MultiAssignmentRuntimeModules.OrderedForRange, "ForRangeStatement", 1 },
            { MultiAssignmentRuntimeModules.OrderedWhile, "WhileStatement", 2 }
        };

    [Theory]
    [MemberData(nameof(FallbackModules))]
    public async Task Mixed_assignment_and_break_body_always_falls_back_as_a_unit(
        string moduleJson)
    {
        using var host = SandboxTestHost.Create();
        var plan = await MultiAssignmentLoopTestRuntime.PrepareAsync(host, moduleJson);
        var interpreter = new SandboxInterpreter();
        SandboxResourceUsage? expectedUsage = null;

        for (var i = 0; i < 3; i++)
        {
            var result = MultiAssignmentLoopTestRuntime.Execute(
                interpreter,
                plan,
                MultiAssignmentLoopTestRuntime.Input(3));

            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(1, ((I32Value)result.Value!).Value);
            Assert.Equal(1, result.ResourceUsage.LoopIterations);
            expectedUsage ??= result.ResourceUsage;
            Assert.Equal(expectedUsage, result.ResourceUsage);
        }
    }

    [Theory]
    [MemberData(nameof(TraceModules))]
    public async Task Debug_trace_keeps_both_assignment_events_after_cache_warmup(
        string moduleJson,
        string loopNode,
        int expectedValue)
    {
        using var host = SandboxTestHost.Create();
        var plan = await MultiAssignmentLoopTestRuntime.PrepareAsync(host, moduleJson);
        var interpreter = new SandboxInterpreter();
        var input = MultiAssignmentLoopTestRuntime.Input(1, 1);

        Assert.True(MultiAssignmentLoopTestRuntime.Execute(interpreter, plan, input).Succeeded);
        Assert.True(MultiAssignmentLoopTestRuntime.Execute(interpreter, plan, input).Succeeded);
        var traced = MultiAssignmentLoopTestRuntime.Execute(
            interpreter,
            plan,
            input,
            MultiAssignmentLoopTestRuntime.Options(debug: true));

        Assert.True(traced.Succeeded, traced.Error?.SafeMessage);
        Assert.Equal(expectedValue, ((I32Value)traced.Value!).Value);
        Assert.Contains(
            traced.AuditEvents,
            audit => MultiAssignmentLoopTestRuntime.IsTrace(audit, loopNode));
        Assert.Equal(
            4,
            traced.AuditEvents.Count(
                audit => MultiAssignmentLoopTestRuntime.IsTrace(audit, "AssignmentStatement")));
    }

    [Fact]
    public async Task Concurrent_first_for_range_executions_publish_a_reusable_plan()
    {
        await AssertConcurrentFirstExecutionsAsync(
            MultiAssignmentRuntimeModules.OrderedForRange,
            first: 100,
            second: 3,
            expectedValue: 399,
            expectedLoops: 100);
    }

    [Fact]
    public async Task Concurrent_first_while_executions_publish_a_reusable_plan()
    {
        await AssertConcurrentFirstExecutionsAsync(
            MultiAssignmentRuntimeModules.OrderedWhile,
            first: 100,
            second: 3,
            expectedValue: 105,
            expectedLoops: 34);
    }

    private static async Task AssertConcurrentFirstExecutionsAsync(
        string moduleJson,
        int first,
        int second,
        int expectedValue,
        long expectedLoops)
    {
        const int executionCount = 8;
        using var host = SandboxTestHost.Create();
        var plan = await MultiAssignmentLoopTestRuntime.PrepareAsync(host, moduleJson);
        var interpreter = new SandboxInterpreter();
        using var ready = new CountdownEvent(executionCount);
        using var start = new ManualResetEventSlim();
        var executions = Enumerable.Range(0, executionCount)
            .Select(_ => Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                return MultiAssignmentLoopTestRuntime.Execute(
                    interpreter,
                    plan,
                    MultiAssignmentLoopTestRuntime.Input(first, second));
            }))
            .ToArray();

        var allReady = ready.Wait(TimeSpan.FromSeconds(10));
        start.Set();
        Assert.True(allReady, "concurrent executions did not reach the shared start gate");
        var results = await Task.WhenAll(executions);

        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(expectedValue, ((I32Value)result.Value!).Value);
            Assert.Equal(expectedLoops, result.ResourceUsage.LoopIterations);
            Assert.Equal(results[0].ResourceUsage, result.ResourceUsage);
        });

        var followUp = MultiAssignmentLoopTestRuntime.Execute(
            interpreter,
            plan,
            MultiAssignmentLoopTestRuntime.Input(first, second));
        Assert.True(followUp.Succeeded, followUp.Error?.SafeMessage);
        Assert.Equal(expectedValue, ((I32Value)followUp.Value!).Value);
        Assert.Equal(results[0].ResourceUsage, followUp.ResourceUsage);
    }
}
