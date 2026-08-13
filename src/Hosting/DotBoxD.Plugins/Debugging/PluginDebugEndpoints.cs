namespace DotBoxD.Plugins.Debugging;

/// <summary>Stable host-provided endpoint for versioned remote-debug request/response messages.</summary>
public interface IPluginDebugControlEndpoint
{
    ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default);
}

/// <summary>Stable plugin-provided endpoint for reverse remote-debug events.</summary>
public interface IPluginDebugEventEndpoint
{
    ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default);
}
