namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    internal void StartPooledTimeoutIfNeeded(PooledPendingResponse pending)
    {
        if (_hasFiniteTimeout && !pending.CompletionStarted)
        {
            _pending.StartTimeout(pending, _timeout);
        }
    }
}
