namespace DotBoxD.Services.Server;

internal sealed class EmptyInstanceLease : IAsyncDisposable
{
    internal static EmptyInstanceLease Instance { get; } = new();

    public ValueTask DisposeAsync() => default;
}

internal sealed class InstanceRegistryLease(InstanceRegistry registry, object instance) : IAsyncDisposable
{
    private InstanceRegistry? _registry = registry;
    private readonly object _instance = instance;

    public ValueTask DisposeAsync()
    {
        var currentRegistry = Interlocked.Exchange(ref _registry, null);
        return currentRegistry is null ? default : currentRegistry.ReleaseLeaseAsync(_instance);
    }
}

internal sealed class InstanceRegistryDisposal(object instance)
{
    internal object Instance { get; } = instance;

    internal TaskCompletionSource<bool> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _isReady;
    private int _disposalStarted;

    internal void MarkReady() => Volatile.Write(ref _isReady, 1);

    internal bool TryStartDisposal() =>
        Volatile.Read(ref _isReady) != 0 &&
        Interlocked.CompareExchange(ref _disposalStarted, 1, 0) == 0;
}
