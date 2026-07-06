using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Peer.PeerInboundTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PeerInboundFrameSenderCoverageTests
{
    [Fact]
    public async Task InboundDispatch_WithFrameSender_DetachesOriginalResponseWriter()
    {
        var serializer = NewSerializer();
        PooledBufferWriter? sentWriter = null;
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task SendMemoryAsync(ReadOnlyMemory<byte> _, CancellationToken __) =>
            throw new InvalidOperationException("Expected pooled frame sender path.");

        ValueTask SendFrameAsync(PooledBufferWriter writer, CancellationToken _)
        {
            sentWriter = writer;
            sent.SetResult();
            return default;
        }

        var streams = new RpcStreamManager(serializer, SendMemoryAsync, exceptionTransformer: null, SendFrameAsync);
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions { InboundQueueCapacity = null, RequestTimeout = ShortTimeout },
            streams,
            SendMemoryAsync,
            SendFrameAsync,
            static (_, _, _, _) => { },
            (_, ex) => dispatchError.TrySetResult(ex));
        inbound.AddDispatcher(new EchoDispatcher());
        inbound.Start(CancellationToken.None);

        Assert.True(await inbound.AcceptRequestAsync(
            CreateRequestFrame(serializer, 61, EchoDispatcher.Service, "Echo"),
            messageId: 61,
            CancellationToken.None));

        await sent.Task.WaitAsync(ShortTimeout);
        await WaitForInboundDrainAsync(inbound);

        Assert.False(dispatchError.Task.IsCompleted);
        Assert.NotNull(sentWriter);
        Assert.True(sentWriter.WrittenCount > 0);

        sentWriter.Dispose();
        await inbound.StopAsync().WaitAsync(ShortTimeout);
    }

    private static async Task WaitForInboundDrainAsync(RpcPeerInboundDispatcher inbound)
    {
        var deadline = DateTimeOffset.UtcNow + ShortTimeout;
        while (inbound.ActiveInboundCount != 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.Equal(0, inbound.ActiveInboundCount);
    }
}
