using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

public sealed class RpcStreamingContextSetResponseCancellationRegressionTests
{
    [Fact]
    public void SetResponseObservesAlreadyCanceledDispatchTokenBeforeAcceptingStream()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = new RpcStreamingContext(streams, serializer, cts.Token);
        var response = new TrackingStream();

        var ex = Record.Exception(() => context.SetResponse(response));

        Assert.IsType<OperationCanceledException>(ex);
        Assert.Null(context.Response);
        Assert.False(response.Disposed);
        AssertNoPendingCreditForReleasedReservation(streams, streamId: 1);
    }

    private static void AssertNoPendingCreditForReleasedReservation(
        RpcStreamManager streams,
        int streamId)
    {
        using var credit = RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            Disposed = true;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
