using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

public sealed class RpcPeerDisconnectedDisposeDeadlockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task DisconnectedHandler_CanSynchronouslyDisposePeerAfterRemoteClose()
    {
        await using var channel = new ScriptedConnection();
        var handlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerFaulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        RpcPeer? peer = null;

        peer = RpcPeer
            .Over(channel, new MessagePackRpcSerializer(), new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();
        peer.Disconnected += (_, _) =>
        {
            handlerEntered.TrySetResult(true);
            try
            {
                peer.DisposeAsync().AsTask().GetAwaiter().GetResult();
                handlerCompleted.TrySetResult(true);
            }
            catch (Exception ex)
            {
                handlerFaulted.TrySetResult(ex);
            }
        };

        channel.Enqueue(Payload.Empty);
        await handlerEntered.Task.WaitAsync(Timeout);
        var terminalTask = await Task
            .WhenAny(handlerCompleted.Task, handlerFaulted.Task, Task.Delay(Timeout));

        Assert.True(
            ReferenceEquals(handlerCompleted.Task, terminalTask),
            "The Disconnected handler did not finish after synchronously disposing the peer.");
        Assert.True(handlerCompleted.Task.IsCompletedSuccessfully);
        Assert.False(handlerFaulted.Task.IsCompleted);
    }
}
