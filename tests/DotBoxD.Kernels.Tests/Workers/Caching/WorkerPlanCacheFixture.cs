using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Workers;

internal sealed class WorkerPlanCacheFixture : IDisposable
{
    internal const int Capacity = WorkerPreparedPlanCache.Capacity;
    private static readonly SandboxPolicy Policy = SandboxPolicyBuilder.Create()
        .WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();
    private static readonly SandboxValue Input = SandboxValue.FromList(
        [SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
    private static readonly SandboxExecutionOptions Options = new() { Mode = ExecutionMode.Interpreted };
    private readonly RecordingInterpreter _interpreter = new();
    private int _factoryCalls;

    internal WorkerPlanCacheFixture()
    {
        Worker = new SandboxHostWorkerClient(() =>
        {
            Interlocked.Increment(ref _factoryCalls);
            return SandboxHost.Create(builder => builder.UseInterpreter(_interpreter));
        });
    }

    internal SandboxHost RequestingHost { get; } = SandboxHost.Create();
    internal SandboxHostWorkerClient Worker { get; }
    internal int FactoryCalls => Volatile.Read(ref _factoryCalls);

    internal ExecutionPlan Prepare(string id)
    {
        var module = RequestingHost.ImportJsonAsync(SandboxTestHost.PureScoreJson(id)).GetAwaiter().GetResult();
        return RequestingHost.PrepareAsync(module, Policy).GetAwaiter().GetResult();
    }

    internal ExecutionPlan Reprepare(ExecutionPlan plan)
        => RequestingHost.PrepareAsync(plan.Module, plan.Policy).GetAwaiter().GetResult();

    internal static ExecutionPlan Copy(ExecutionPlan plan) => new(
        plan.ModuleHash, plan.PlanHash, plan.PlanSeal, plan.PolicyHash, plan.BindingManifestHash,
        plan.Module, plan.Policy, plan.Bindings, plan.Budget, plan.FunctionAnalysis, plan.BindingReferences);

    internal void Execute(ExecutionPlan plan)
    {
        var result = Worker.ExecuteInWorkerAsync(plan, "main", Input, Options).GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
    }

    internal ExecutionPlan ObservedPlan()
    {
        Assert.True(_interpreter.LastPlan.TryGetTarget(out var plan));
        return plan;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal WeakReference[] ExecuteAndRelease(string id)
    {
        var plan = Prepare(id);
        Execute(plan);
        return [new WeakReference(plan), new WeakReference(plan.Module), new WeakReference(ObservedPlan())];
    }

    internal void FillCache()
    {
        for (var i = 0; i < Capacity; i++)
        {
            Execute(Prepare($"filler-{i}"));
        }
    }

    internal static async Task AssertCollectedAsync(WeakReference[] references)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (references.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.All(references, reference => Assert.False(reference.IsAlive,
            "Evicted or disposed worker cache entries must release their plans and modules."));
    }

    public void Dispose()
    {
        Worker.Dispose();
        RequestingHost.Dispose();
    }

    private sealed class RecordingInterpreter : ISandboxInterpreter
    {
        private readonly SandboxInterpreter _inner = new();
        internal WeakReference<ExecutionPlan> LastPlan { get; } = new(null!);

        public ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan, string entrypoint, SandboxValue input,
            SandboxExecutionOptions options, CancellationToken cancellationToken)
        {
            LastPlan.SetTarget(plan);
            return _inner.ExecuteAsync(plan, entrypoint, input, options, cancellationToken);
        }
    }
}
