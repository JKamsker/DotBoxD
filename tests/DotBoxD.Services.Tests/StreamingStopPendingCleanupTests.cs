using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming;
using DotBoxD.Codecs.MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests;

public sealed class StreamingStopPendingCleanupTests
{
    [Fact]
    public void Stop_Clears_PendingCredits()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        using var creditFrame = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 2);
        Assert.True(streams.TryAddCredit(creditFrame));
        Assert.Equal(1, streams.PendingCreditCount);
        streams.Stop();
        Assert.Equal(0, streams.PendingCreditCount);
    }

    [Fact]
    public void Stop_Clears_ReservedOutbound()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        streams.Stop();
        var ex = Record.Exception(() => streams.ReserveOutbound(handle.StreamId));
        Assert.Null(ex);
    }

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
