using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompiledNoAuditResultRunnerAllocationTests
{
    private const int WarmupIterations = 5_000;
    private const int MeasuredIterations = 100_000;
    private const long FreshBytesPerRun = 512;
    private const long ReusedBytesPerRun = 192;
    private const double MeasurementNoiseBytesPerRun = 0.1;

    [Fact]
    public async Task Reusable_state_removes_meter_and_context_allocations_from_full_results()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());
        var artifact = CompiledArtifactTestFactory.DynamicMethod(
            plan,
            static (context, _) =>
            {
                context.ChargeFuel(1);
                return SandboxValue.FromInt32(35);
            });
        var executable = new CompiledExecutable(artifact, "Miss");
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var allowedBindings = AssertNoAuditBindings(plan);
        var reusableState = new CompiledNoAuditRunState(plan);
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Auto,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        _ = Measure(executable, plan, input, options, allowedBindings, null, WarmupIterations);
        _ = Measure(executable, plan, input, options, allowedBindings, reusableState, WarmupIterations);
        ForceGc();

        var fresh = Measure(executable, plan, input, options, allowedBindings, null, MeasuredIterations);
        var reused = Measure(
            executable,
            plan,
            input,
            options,
            allowedBindings,
            reusableState,
            MeasuredIterations);
        var freshBytesPerRun = fresh.AllocatedBytes / (double)MeasuredIterations;
        var reusedBytesPerRun = reused.AllocatedBytes / (double)MeasuredIterations;

        Console.WriteLine(
            $"full-result no-audit runner: fresh={freshBytesPerRun:N3} B/run " +
            $"({fresh.AllocatedBytes:N0} B), reused={reusedBytesPerRun:N3} B/run " +
            $"({reused.AllocatedBytes:N0} B), saved={freshBytesPerRun - reusedBytesPerRun:N3} B/run.");
        Assert.InRange(
            freshBytesPerRun,
            FreshBytesPerRun,
            FreshBytesPerRun + MeasurementNoiseBytesPerRun);
        Assert.InRange(
            reusedBytesPerRun,
            ReusedBytesPerRun,
            ReusedBytesPerRun + MeasurementNoiseBytesPerRun);
        Assert.InRange(
            freshBytesPerRun - reusedBytesPerRun,
            FreshBytesPerRun - ReusedBytesPerRun - MeasurementNoiseBytesPerRun,
            FreshBytesPerRun - ReusedBytesPerRun + MeasurementNoiseBytesPerRun);
        GC.KeepAlive(fresh.Checksum + reused.Checksum);
    }

    private static AllocationSummary Measure(
        CompiledExecutable executable,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        IReadOnlySet<string> allowedBindings,
        CompiledNoAuditRunState? reusableState,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var pending = CompiledNoAuditResultRunner.Execute(
                executable,
                plan,
                "main",
                input,
                options,
                allowedBindings,
                CancellationToken.None,
                reusableState);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("No-audit compiled execution unexpectedly became asynchronous.");
            }

            var result = pending.Result;
            if (!result.Succeeded || result.Value is not I32Value { Value: 35 })
            {
                throw new InvalidOperationException("No-audit compiled execution changed its result.");
            }

            checksum += result.ResourceUsage.FuelUsed;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new AllocationSummary(allocated, checksum);
    }

    private static IReadOnlySet<string> AssertNoAuditBindings(ExecutionPlan plan)
    {
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        Assert.NotNull(allowedBindings);
        Assert.Empty(allowedBindings);
        return allowedBindings;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct AllocationSummary(long AllocatedBytes, long Checksum);
}
