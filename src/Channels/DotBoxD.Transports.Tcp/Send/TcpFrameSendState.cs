using DotBoxD.Services.Buffers;

namespace DotBoxD.Transports.Tcp;

internal enum TcpFrameSendStage : byte
{
    None,
    Gate,
    Write,
}

internal struct TcpFrameSendState
{
    public TcpConnection? Connection;
    public PooledBufferWriter? Frame;
    public ReadOnlyMemory<byte> Data;

    // The caller's token covers only the gate wait; a started write is never cancelled by it.
    public CancellationToken CancellationToken;
    public TcpFrameSendStage PendingStage;
    public bool OwnsGate;

    // Set once WriteAsync was issued: a failure after this point may have left a partial frame on the wire.
    public bool WriteStarted;

    public void Clear() => this = default;
}
