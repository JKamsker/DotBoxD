using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal sealed record InterpreterHostBoundaryInvariant(ExecutionPlan Plan)
{
    private static readonly ResourceUsageInvariant ExpectedUsage = new(
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

    public long Validate(SandboxExecutionResult result)
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
            result.AuditEvents.Count != 0 ||
            !StringComparer.Ordinal.Equals(result.ModuleHash, Plan.ModuleHash) ||
            !StringComparer.Ordinal.Equals(result.PlanHash, Plan.PlanHash) ||
            !StringComparer.Ordinal.Equals(result.PolicyHash, Plan.PolicyHash))
        {
            throw new InvalidOperationException("interpreter host-boundary result envelope changed");
        }

        var usage = ResourceUsageInvariant.From(result.ResourceUsage);
        if (usage != ExpectedUsage)
        {
            throw new InvalidOperationException(
                $"expected interpreter host-boundary resource usage {ExpectedUsage}, got {usage}");
        }

        return 7;
    }
}

internal readonly record struct InterpreterHostBoundaryMeasurement(
    double ElapsedMilliseconds,
    long AllocatedBytes,
    long Checksum)
{
    public double NanosecondsPerOperation(int iterations)
        => ElapsedMilliseconds * 1_000_000D / iterations;

    public double BytesPerOperation(int iterations)
        => AllocatedBytes / (double)iterations;
}
