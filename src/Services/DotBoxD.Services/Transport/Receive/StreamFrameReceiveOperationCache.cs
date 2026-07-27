namespace DotBoxD.Services.Transport;

/// <summary>Retains Stream receive sources for a connection that reached shared-pool overflow.</summary>
internal sealed class StreamFrameReceiveOperationCache :
    DedicatedFrameReceiveOperationCache<StreamFrameReceiveOperation>
{
    protected override StreamFrameReceiveOperation CreateOperation() => new();
}
