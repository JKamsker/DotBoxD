using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Execution;

internal static class PreparedPlanCacheFixture
{
    private static readonly SandboxPolicy Policy = SandboxPolicyBuilder.Create()
        .WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();

    internal static SandboxHost CreateHost() => SandboxTestHost.Create();

    internal static ExecutionPlan Prepare(SandboxHost host, string id)
    {
        var module = host.ImportJsonAsync(SandboxTestHost.PureScoreJson(id)).GetAwaiter().GetResult();
        return host.PrepareAsync(module, Policy).GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference[] PrepareAndRelease(SandboxHost host, int count, bool execute)
    {
        var references = new List<WeakReference>();
        for (var i = 0; i < count; i++)
        {
            var plan = Prepare(host, $"released-{i}");
            if (execute)
            {
                Execute(host, plan);
            }
            references.Add(new WeakReference(plan));
            references.Add(new WeakReference(plan.Module));
        }
        return references.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference[] PrepareRetained(SandboxHost host, PlanHolder holder, bool copy)
    {
        var original = Prepare(host, "retained");
        holder.Plan = copy ? Copy(original) : original;
        return [new WeakReference(original), new WeakReference(holder.Plan), new WeakReference(original.Module)];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Execute(SandboxHost host, ExecutionPlan plan)
    {
        var result = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, AllowFallbackToInterpreter = false })
            .GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ExecuteHeld(SandboxHost host, PlanHolder holder) => Execute(host, holder.Plan!);

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
            "An idle host must not retain released plan identities and modules indefinitely."));
    }

    private static ExecutionPlan Copy(ExecutionPlan plan) => new(
        plan.ModuleHash, plan.PlanHash, plan.PlanSeal, plan.PolicyHash, plan.BindingManifestHash,
        plan.Module, plan.Policy, plan.Bindings, plan.Budget, plan.FunctionAnalysis, plan.BindingReferences);

    internal sealed class PlanHolder
    {
        internal ExecutionPlan? Plan { get; set; }
    }
}
