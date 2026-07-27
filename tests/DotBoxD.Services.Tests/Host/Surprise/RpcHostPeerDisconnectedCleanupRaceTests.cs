using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostPeerDisconnectedCleanupRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DisposeAsync_WaitsForPeerCleanupBlockedInPeerDisconnected()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = RpcHost.Listen(
            new SingleConnectionServerTransport(serverConnection),
            new MessagePackRpcSerializer());
        Task? disposeTask = null;

        host.PeerConnected += (_, _) => connected.TrySetResult();
        host.PeerDisconnected += (_, _) =>
        {
            disconnectedEntered.TrySetResult();
            releaseDisconnected.Task.GetAwaiter().GetResult();
        };

        try
        {
            await host.StartAsync();
            await connected.Task.WaitAsync(Timeout);

            await clientConnection.DisposeAsync();
            await disconnectedEntered.Task.WaitAsync(Timeout);

            disposeTask = host.DisposeAsync().AsTask();
            await Task.Delay(100);

            Assert.False(
                disposeTask.IsCompleted,
                "Host disposal must wait for disconnected-peer cleanup already owned by the host.");
            Assert.True(serverConnection.IsConnected);

            releaseDisconnected.SetResult();
            await disposeTask.WaitAsync(Timeout);
            await WaitForAsync(() => !serverConnection.IsConnected);
        }
        finally
        {
            releaseDisconnected.TrySetResult();
            if (disposeTask is not null)
            {
                await disposeTask.WaitAsync(Timeout);
            }

            await host.DisposeAsync();
            await clientConnection.DisposeAsync();
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!predicate())
        {
            await Task.Delay(10, cts.Token);
        }
    }
}
