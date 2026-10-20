using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.UI.Runtime;

namespace Examples.SandboxedUi.Host;

internal sealed class UiSessionConnection : IAsyncDisposable
{
    private readonly RpcPeer _peer;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public UiSessionConnection(RpcPeer peer, UiSession session)
    {
        _peer = peer;
        peer.Disconnected += OnDisconnected;
        Released = ReleaseSessionAsync(session);
        // Cover a disconnect before/during installation or before the subscription was attached.
        if (!peer.IsConnected)
        { _closed.TrySetResult(); }
    }

    public Task Released { get; }

    private void OnDisconnected(object? sender, RpcDisconnectedEventArgs args) => _closed.TrySetResult();

    private async Task ReleaseSessionAsync(UiSession session)
    {
        await _closed.Task.ConfigureAwait(false);
        _peer.Disconnected -= OnDisconnected;
        await session.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _peer.Disconnected -= OnDisconnected;
        _closed.TrySetResult();
        await Released.ConfigureAwait(false);
    }
}
