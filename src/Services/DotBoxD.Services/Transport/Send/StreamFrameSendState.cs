using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>Mutable owned-frame send state that transfers to a completion source on suspension.</summary>
internal struct StreamFrameSendState
{
    public StreamConnection? Connection;
    public PooledBufferWriter? Frame;
    public ReadOnlyMemory<byte> Data;
    public CancellationToken CallerToken;
    public StreamFrameSendStage Stage;
    public bool GateHeld;
}

internal enum StreamFrameSendStage
{
    AcquireGate,
    Write,
    Flush,
    Completed,
}
