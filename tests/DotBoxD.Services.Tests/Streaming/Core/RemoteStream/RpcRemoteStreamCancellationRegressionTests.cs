using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcRemoteStreamCancellationRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ReadAsync_WithPreCanceledToken_DoesNotConsumeBufferedChunkBytes()
    {
        var streams = new RpcStreamManager(
            new MessagePackRpcSerializer(),
            SendNoopAsync,
            exceptionTransformer: null);
        var handle = new RpcStreamHandle(73_001, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0x41, 0x42 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        using var remoteStream = new RpcRemoteStream(receiver);
        var first = new byte[1];
        var firstRead = await remoteStream.ReadAsync(first, CancellationToken.None)
            .AsTask()
            .WaitAsync(Timeout);
        Assert.Equal(1, firstRead);
        Assert.Equal(0x41, first[0]);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var canceledBuffer = new byte[1];

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            remoteStream.ReadAsync(canceledBuffer, cts.Token).AsTask().WaitAsync(Timeout));

        var second = new byte[1];
        var secondRead = await remoteStream.ReadAsync(second, CancellationToken.None)
            .AsTask()
            .WaitAsync(Timeout);
        Assert.Equal(1, secondRead);
        Assert.Equal(0x42, second[0]);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
