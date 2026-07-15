using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LazyAudit;

public sealed class InterpreterLazyAuditConcurrencyTests
{
    private const int ExecutionCount = 32;

    [Fact]
    public async Task Concurrent_pure_successes_and_failures_keep_audit_state_isolated()
    {
        using var host = SandboxTestHost.Create();
        var plan = await InterpreterLazyAuditTestSupport.PreparePureAsync(host);
        var interpreter = new SandboxInterpreter();
        var failureRunIds = Enumerable.Range(0, ExecutionCount)
            .Select(_ => SandboxRunId.New())
            .ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        var executions = Enumerable.Range(0, ExecutionCount)
            .Select(RunAsync)
            .ToArray();
        await allReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();
        var outcomes = await Task.WhenAll(executions);

        var expectedSuccessUsage = outcomes.First(outcome => !outcome.ShouldFail).Result.ResourceUsage;
        var expectedFailureUsage = outcomes.First(outcome => outcome.ShouldFail).Result.ResourceUsage;
        foreach (var outcome in outcomes)
        {
            if (!outcome.ShouldFail)
            {
                Assert.True(outcome.Result.Succeeded, outcome.Result.Error?.SafeMessage);
                Assert.Equal(outcome.Index, ((I32Value)outcome.Result.Value!).Value);
                Assert.Empty(outcome.Result.AuditEvents);
                Assert.Equal(expectedSuccessUsage, outcome.Result.ResourceUsage);
                continue;
            }

            Assert.False(outcome.Result.Succeeded);
            Assert.Equal(SandboxErrorCode.InvalidInput, outcome.Result.Error!.Code);
            var summary = Assert.Single(outcome.Result.AuditEvents);
            Assert.Equal("RunSummary", summary.Kind);
            Assert.Equal(failureRunIds[outcome.Index], summary.RunId);
            Assert.Equal(1, summary.SequenceNumber);
            Assert.Equal(expectedFailureUsage, outcome.Result.ResourceUsage);
        }

        Assert.Equal(
            ExecutionCount / 2,
            outcomes.Where(outcome => outcome.ShouldFail)
                .Select(outcome => outcome.Result.AuditEvents[0].RunId)
                .Distinct()
                .Count());

        async Task<Outcome> RunAsync(int index)
        {
            if (Interlocked.Increment(ref readyCount) == ExecutionCount)
            {
                allReady.SetResult();
            }

            await start.Task;
            var shouldFail = index % 2 != 0;
            var input = shouldFail ? SandboxValue.Unit : SandboxValue.FromInt32(index);
            var options = InterpreterLazyAuditTestSupport.SuppressedOptions(
                shouldFail ? failureRunIds[index] : null);
            var result = await interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            return new Outcome(index, shouldFail, result);
        }
    }

    private sealed record Outcome(int Index, bool ShouldFail, SandboxExecutionResult Result);
}
