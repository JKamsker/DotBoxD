namespace SafeIR;

public sealed record SandboxResourceUsage(
    long FuelUsed,
    long MaxFuel,
    long AllocatedBytes,
    int HostCalls,
    long FileBytesRead,
    long FileBytesWritten,
    long NetworkBytesRead,
    int LogEvents,
    long CollectionElements,
    long StringBytes);
