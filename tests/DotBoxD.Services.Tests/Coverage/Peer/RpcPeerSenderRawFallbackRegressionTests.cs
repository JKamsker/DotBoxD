using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class RpcPeerSenderRawFallbackRegressionTests
{
    [Fact]
    public async Task Raw_only_channel_rejects_malformed_owned_frame_before_send_and_disposes_frame()
    {
        var channel = new RecordingRawChannel();
        using var sender = new RpcPeerSender(channel, static () => false);
        var frame = CreateMalformedFrame();

        Assert.Null(sender.ValidatedFrameSender);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sender.SendFrameValueAsync(frame, CancellationToken.None).AsTask());

        Assert.Equal(0, channel.SendCalls);
        AssertDisposed(frame);
    }

    private static PooledBufferWriter CreateMalformedFrame()
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.StreamComplete, ReadOnlySpan<byte>.Empty);
        frame.WrittenSpan[0]++;
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);

    private sealed class RecordingRawChannel : IRpcChannel
    {
        public bool IsConnected => true;

        public string RemoteEndpoint => "raw-only://test";

        public int SendCalls { get; private set; }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendCalls++;
            return Task.CompletedTask;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Payload.Empty);

        public ValueTask DisposeAsync() => default;
    }
}
