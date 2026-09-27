using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.FailureObservation;

public sealed class RpcOwnedSendFailureObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Immediate_send_failure_observes_response_and_releases_admission(bool request)
        => ResponseFailureObservation.AssertObservedAsync(marker => Exercise(request, marker));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Exercise(bool request, string marker)
    {
        var serializer = new MessagePackRpcSerializer();
        var expected = new IOException("Controlled send failure");
        var responseError = new IOException(marker);
        var streams = new RpcStreamManager(serializer, static (_, _) => Task.CompletedTask, exceptionTransformer: null);
        RpcPeerOutboundInvoker? invoker = null;
        var sends = 0;
        invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { MaxPendingRequests = 1 },
            ensureStarted: static () => { },
            static (_, _) => Task.CompletedTask,
            (frame, _) =>
            {
                sends++;
                invoker!.FailPending(responseError);
                throw expected;
            },
            streams);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var failure = Record.Exception(() =>
                {
                    var call = request
                        ? invoker.InvokeAsync<int, int>("Service", "Method", 123)
                        : invoker.InvokeAsync<int>("Service", "Method");
                    call.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                });
                Assert.Same(expected, failure);
            }

            Assert.Equal(2, sends);
        }
        finally
        {
            invoker.StopCancelFramesAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            streams.Stop();
        }

        return new WeakReference(responseError);
    }
}
