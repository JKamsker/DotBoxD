using System.Runtime.CompilerServices;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Workers;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class WorkerPendingCallAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Pending_call_setup_keeps_its_allocation_cost_bounded()
    {
        var plan = WorkerHostLifetimeFixture.Prepare();
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            Isolation = SandboxIsolation.WorkerProcess,
            RunId = SandboxRunId.New(),
        };
        using var adapter = new SandboxHostWorkerClient(() => SandboxHost.Create());
        var result = await adapter.ExecuteInWorkerAsync(plan, "main", input,
            options with { Isolation = SandboxIsolation.InProcess });
        ConfiguredWorkerLifetimeFixture.AssertSuccess(result);
        var worker = new PendingWorker();
        using var executor = new SandboxWorkerExecutor(new ConfiguredSandboxWorker(
            worker, SandboxWorkerProfile.HardenedOutOfProcess));
        _ = Measure(executor, worker, plan, input, options, result, 1_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            minimum = Math.Min(minimum, Measure(executor, worker, plan, input, options, result, 2_000));
        }

        output.WriteLine($"Starting a pending worker call: {minimum / 2_000d} B/call.");
        Assert.InRange(minimum, 0, 608L * 2_000);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(
        SandboxWorkerExecutor executor, PendingWorker worker, ExecutionPlan plan, SandboxValue input,
        SandboxExecutionOptions options, SandboxExecutionResult result, int count)
    {
        long allocated = 0;
        for (var index = 0; index < count; index++)
        {
            worker.Completion = new TaskCompletionSource<SandboxExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var pending = executor.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(pending.IsCompleted);
            worker.Completion.SetResult(result);
            ConfiguredWorkerLifetimeFixture.AssertSuccess(pending.AsTask().GetAwaiter().GetResult());
        }
        return allocated;
    }

    private sealed class PendingWorker : ISandboxWorkerClient
    {
        internal TaskCompletionSource<SandboxExecutionResult> Completion { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan, string entrypoint, SandboxValue input, SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
            => new(Completion.Task);
    }
}
