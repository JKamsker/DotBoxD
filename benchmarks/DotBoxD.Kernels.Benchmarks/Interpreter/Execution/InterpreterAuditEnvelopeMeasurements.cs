using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal enum AuditShape
{
    Empty,
    SuccessSummary,
    SuccessSummaryWithExplicitRunId,
    FailureSummary,
    DebugTrace,
    SandboxLog
}

internal readonly record struct ExpectedOutcome(
    bool Succeeded,
    long Checksum,
    SandboxErrorCode? ErrorCode)
{
    public static ExpectedOutcome I32(int value) => new(true, value, null);

    public static ExpectedOutcome Unit() => new(true, 1, null);

    public static ExpectedOutcome Failure(SandboxErrorCode errorCode)
        => new(false, -(long)errorCode - 1, errorCode);

    public long Validate(SandboxExecutionResult result)
    {
        if (Succeeded &&
            result is { Succeeded: true, Error: null, Value: I32Value value } &&
            value.Value == Checksum)
        {
            return Checksum;
        }

        if (Succeeded &&
            Checksum == 1 &&
            result is { Succeeded: true, Error: null } &&
            ReferenceEquals(result.Value, SandboxValue.Unit))
        {
            return Checksum;
        }

        if (!Succeeded &&
            result is { Succeeded: false, Value: null, Error.Code: var code } &&
            code == ErrorCode)
        {
            return Checksum;
        }

        throw new InvalidOperationException(result.Error?.SafeMessage ?? "unexpected execution result");
    }
}

internal readonly record struct ResourceUsageInvariant(
    long FuelUsed,
    long MaxFuel,
    long LoopIterations,
    long AllocatedBytes,
    int HostCalls,
    long FileBytesRead,
    long FileBytesWritten,
    long NetworkBytesRead,
    long NetworkBytesWritten,
    int LogEvents,
    long CollectionElements,
    long StringBytes)
{
    public static ResourceUsageInvariant From(SandboxResourceUsage usage)
        => new(
            usage.FuelUsed,
            usage.MaxFuel,
            usage.LoopIterations,
            usage.AllocatedBytes,
            usage.HostCalls,
            usage.FileBytesRead,
            usage.FileBytesWritten,
            usage.NetworkBytesRead,
            usage.NetworkBytesWritten,
            usage.LogEvents,
            usage.CollectionElements,
            usage.StringBytes);
}

internal readonly record struct Measurement(
    double ElapsedMilliseconds,
    long Bytes,
    long Checksum,
    ResourceUsageInvariant Usage);

internal readonly record struct EnvelopeMeasurement(
    double ElapsedMilliseconds,
    long Bytes,
    long Checksum);
