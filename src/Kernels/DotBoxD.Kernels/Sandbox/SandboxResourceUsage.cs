namespace DotBoxD.Kernels.Sandbox;

public sealed record SandboxResourceUsage(
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
    private const string ResourceUsageCounterMessage = "resource usage counters must be non-negative.";

    private long _fuelUsed = RequireNonNegative(FuelUsed, nameof(FuelUsed));
    private long _maxFuel = RequireNonNegative(MaxFuel, nameof(MaxFuel));
    private long _loopIterations = RequireNonNegative(LoopIterations, nameof(LoopIterations));
    private long _allocatedBytes = RequireNonNegative(AllocatedBytes, nameof(AllocatedBytes));
    private int _hostCalls = RequireNonNegative(HostCalls, nameof(HostCalls));
    private long _fileBytesRead = RequireNonNegative(FileBytesRead, nameof(FileBytesRead));
    private long _fileBytesWritten = RequireNonNegative(FileBytesWritten, nameof(FileBytesWritten));
    private long _networkBytesRead = RequireNonNegative(NetworkBytesRead, nameof(NetworkBytesRead));
    private long _networkBytesWritten = RequireNonNegative(NetworkBytesWritten, nameof(NetworkBytesWritten));
    private int _logEvents = RequireNonNegative(LogEvents, nameof(LogEvents));
    private long _collectionElements = RequireNonNegative(CollectionElements, nameof(CollectionElements));
    private long _stringBytes = RequireNonNegative(StringBytes, nameof(StringBytes));

    public long FuelUsed { get => _fuelUsed; init => _fuelUsed = RequireNonNegative(value, nameof(FuelUsed)); }
    public long MaxFuel { get => _maxFuel; init => _maxFuel = RequireNonNegative(value, nameof(MaxFuel)); }
    public long LoopIterations { get => _loopIterations; init => _loopIterations = RequireNonNegative(value, nameof(LoopIterations)); }
    public long AllocatedBytes { get => _allocatedBytes; init => _allocatedBytes = RequireNonNegative(value, nameof(AllocatedBytes)); }
    public int HostCalls { get => _hostCalls; init => _hostCalls = RequireNonNegative(value, nameof(HostCalls)); }
    public long FileBytesRead { get => _fileBytesRead; init => _fileBytesRead = RequireNonNegative(value, nameof(FileBytesRead)); }
    public long FileBytesWritten { get => _fileBytesWritten; init => _fileBytesWritten = RequireNonNegative(value, nameof(FileBytesWritten)); }
    public long NetworkBytesRead { get => _networkBytesRead; init => _networkBytesRead = RequireNonNegative(value, nameof(NetworkBytesRead)); }
    public long NetworkBytesWritten { get => _networkBytesWritten; init => _networkBytesWritten = RequireNonNegative(value, nameof(NetworkBytesWritten)); }
    public int LogEvents { get => _logEvents; init => _logEvents = RequireNonNegative(value, nameof(LogEvents)); }
    public long CollectionElements { get => _collectionElements; init => _collectionElements = RequireNonNegative(value, nameof(CollectionElements)); }
    public long StringBytes { get => _stringBytes; init => _stringBytes = RequireNonNegative(value, nameof(StringBytes)); }

    private static long RequireNonNegative(long value, string paramName)
        => SandboxCounterGuards.RequireNonNegative(value, paramName, ResourceUsageCounterMessage);

    private static int RequireNonNegative(int value, string paramName)
        => SandboxCounterGuards.RequireNonNegative(value, paramName, ResourceUsageCounterMessage);
}
