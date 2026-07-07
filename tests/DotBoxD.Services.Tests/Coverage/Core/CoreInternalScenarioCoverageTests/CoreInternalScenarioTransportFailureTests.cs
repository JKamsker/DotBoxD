using DotBoxD.Services.Peer;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.CoreInternalScenarioTestSupport;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class CoreInternalScenarioTransportFailureTests
{
    [Fact]
    public async Task CancelFrame_SendThrows_IsSwallowed_AndPeerStaysUsable()
    {
        var serializer = NewSerializer();
        await using var channel = new CancelSendControlChannel(faultCancelSends: true);
        await using var peer = RpcPeer
            .Over(channel, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        using var cts = new CancellationTokenSource();
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token);

        // Make sure the request frame is on the wire before cancelling, so the cancel-frame path runs.
        await channel.RequestSent.Task.WaitAsync(Timeout5s);
        cts.Cancel();

        // The call still fails with cancellation even though emitting the cancel frame threw internally.
        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Timeout10s));

        // The cancel-frame send was attempted (and threw); the peer remains alive and usable.
        await channel.CancelSendAttempted.Task.WaitAsync(Timeout10s);
        Assert.True(peer.IsConnected);

        // A follow-up call still works (the swallowed fault did not corrupt the peer).
        var followUp = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        channel.EnqueueResponse(serializer, messageId: 2, result: "ok");
        Assert.Equal("ok", await followUp.WaitAsync(Timeout10s));
    }

}
