using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Retains TCP receive sources for a connection that reached shared-pool overflow.</summary>
internal sealed class TcpFrameReceiveOperationCache :
    DedicatedFrameReceiveOperationCache<TcpFrameReceiveOperation>
{
    protected override TcpFrameReceiveOperation CreateOperation() => new();
}
