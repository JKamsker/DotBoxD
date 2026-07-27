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
    public CancellationToken CancellationToken;
    public TcpFrameSendStage PendingStage;
    public bool OwnsGate;

    public void Clear() => this = default;
}
