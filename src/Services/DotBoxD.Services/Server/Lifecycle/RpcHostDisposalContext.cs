namespace DotBoxD.Services.Server;

internal sealed class RpcHostDisposalContext
{
    private readonly AsyncLocal<bool> _disposingListener = new();

    internal bool IsDisposingListener => _disposingListener.Value;

    internal async Task DisposeListenerAsync(IAsyncDisposable listener)
    {
        _disposingListener.Value = true;
        try
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _disposingListener.Value = false;
        }
    }
}
