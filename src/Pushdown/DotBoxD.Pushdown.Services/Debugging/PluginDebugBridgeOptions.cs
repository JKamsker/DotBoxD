namespace DotBoxD.Pushdown.Services;

/// <summary>Opt-in local bridge limits used only when launch tooling requests kernel debugging.</summary>
public sealed record PluginDebugBridgeOptions
{
    public int MaxFrameBytes { get; init; } = 1024 * 1024;

    public bool WaitForDebuggerBeforeInstall { get; init; } = true;

    public TimeSpan DebuggerWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional client-side source reader used for checksum verification.</summary>
    public Func<string, byte[]?>? SourceReader { get; init; }

    internal void Validate()
    {
        if (MaxFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFrameBytes));
        }

        _ = PluginDebugBridgeProtocol.WrappedEnvelopeLimit(MaxFrameBytes);

        if (DebuggerWaitTimeout <= TimeSpan.Zero || DebuggerWaitTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(DebuggerWaitTimeout));
        }
    }
}

/// <summary>High-entropy endpoint and bearer token passed directly from launch tooling to the DAP adapter.</summary>
public sealed record PluginDebugBridgeDescriptor(int ProcessId, string PipeName, string DiscoveryToken)
{
    public string? DiscoveryFile { get; init; }
}
