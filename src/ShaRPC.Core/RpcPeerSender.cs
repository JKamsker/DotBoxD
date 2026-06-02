using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerSender : IDisposable
{
    private readonly IRpcChannel _channel;
    private readonly Func<bool> _isClosed;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public RpcPeerSender(IRpcChannel channel, Func<bool> isClosed)
    {
        _channel = channel;
        _isClosed = isClosed;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        // Fast-fail once the peer is closing so an outbound call started during teardown does not
        // park in WaitAsync (with a non-cancellable token) only to strand on a disposed send lock.
        if (_isClosed())
        {
            throw new ShaRpcConnectionException("Connection closed.");
        }

        try
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // DisposeAsync disposed the send lock while this send raced teardown; surface the
            // connection contract rather than leaking ObjectDisposedException to the caller.
            throw new ShaRpcConnectionException("Connection closed.");
        }

        try
        {
            if (_isClosed())
            {
                throw new ShaRpcConnectionException("Connection closed.");
            }

            await _channel.SendAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose() => _sendLock.Dispose();
}
