using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class TrustedInterpreterBoundaryAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;
    private const double MaximumHostBytesPerRun = 673;
    private const double MaximumBoundaryBytesPerRun = 1;
    private static readonly SandboxExecutionOptions Options =
        TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: SandboxRunId.New());

    [Fact]
    public async Task Warmed_binding_free_suppressed_success_has_negligible_host_boundary_allocation()
    {
        using var host = TrustedInterpreterBoundaryTestSupport.CreateBuiltInHost();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule,
            policy);
        Assert.True(plan.BindingReferences.TryGetValue("main", out var references));
        Assert.Empty(references);
        var invariant = new ResultInvariant(plan);
        var directInterpreter = new SandboxInterpreter();

        _ = MeasureHost(host, invariant, WarmupIterations);
        _ = MeasureDirect(directInterpreter, invariant, WarmupIterations);

        ForceGc();
        var direct = MeasureDirect(directInterpreter, invariant, MeasurementIterations);
        ForceGc();
        var hosted = MeasureHost(host, invariant, MeasurementIterations);
        var directBytesPerRun = direct.AllocatedBytes / (double)MeasurementIterations;
        var hostBytesPerRun = hosted.AllocatedBytes / (double)MeasurementIterations;

        Assert.Equal(7L * MeasurementIterations, direct.Checksum);
        Assert.Equal(direct.Checksum, hosted.Checksum);
        Assert.True(
            hostBytesPerRun <= MaximumHostBytesPerRun,
            $"Expected at most {MaximumHostBytesPerRun:F1} B/run, observed {hostBytesPerRun:F3} B/run.");
        Assert.True(
            hostBytesPerRun - directBytesPerRun <= MaximumBoundaryBytesPerRun,
            $"Expected at most {MaximumBoundaryBytesPerRun:F1} host-boundary B/run, " +
            $"observed {hostBytesPerRun - directBytesPerRun:F3} B/run " +
            $"(host {hostBytesPerRun:F3}, direct {directBytesPerRun:F3}).");
    }

    private static Measurement MeasureHost(
        DotBoxD.Hosting.Execution.SandboxHost host,
        ResultInvariant invariant,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(
                invariant.Plan,
                "main",
                SandboxValue.Unit,
                Options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Public host execution unexpectedly became asynchronous.");
            }

            checksum += invariant.Validate(pending.Result);
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static Measurement MeasureDirect(
        SandboxInterpreter interpreter,
        ResultInvariant invariant,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                invariant.Plan,
                "main",
                SandboxValue.Unit,
                Options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Direct interpreter execution unexpectedly became asynchronous.");
            }

            checksum += invariant.Validate(pending.Result);
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record ResultInvariant(ExecutionPlan Plan)
    {
        private static readonly SandboxResourceUsage ExpectedUsage = new(
            FuelUsed: 3,
            MaxFuel: long.MaxValue,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: 0,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            CollectionElements: 0,
            StringBytes: 0);

        public int Validate(SandboxExecutionResult result)
        {
            if (result is not
                {
                    Succeeded: true,
                    Error: null,
                    Value: I32Value { Value: 7 },
                    ActualMode: ExecutionMode.Interpreted,
                    ExecutionDispatched: true,
                    ArtifactHash: null
                } ||
                !ReferenceEquals(result.AuditEvents, InMemoryAuditSink.EmptyEventSnapshot) ||
                !StringComparer.Ordinal.Equals(result.ModuleHash, Plan.ModuleHash) ||
                !StringComparer.Ordinal.Equals(result.PlanHash, Plan.PlanHash) ||
                !StringComparer.Ordinal.Equals(result.PolicyHash, Plan.PolicyHash) ||
                result.ResourceUsage != ExpectedUsage)
            {
                throw new InvalidOperationException("Trusted interpreter result invariants changed.");
            }

            return 7;
        }
    }

    private readonly record struct Measurement(long AllocatedBytes, long Checksum);
}
