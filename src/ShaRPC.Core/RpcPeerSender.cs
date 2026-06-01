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
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
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
