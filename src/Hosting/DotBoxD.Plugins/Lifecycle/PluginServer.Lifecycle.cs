namespace DotBoxD.Plugins;

public sealed partial class PluginServer
{
    private readonly object _lifecycleGate = new();

    private bool TryBeginDispose()
    {
        lock (_lifecycleGate)
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0;
        }
    }
}
