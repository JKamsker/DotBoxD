using System.Buffers;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Support;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Peer.PeerOutboundTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PeerOutboundPendingLimitCoverageTests
{
    [Fact]
    public async Task InvokeAsync_ExceedingMaxPendingRequests_ThrowsServiceException()
    {
        var serializer = NewSerializer();
        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(
            channel,
            serializer,
            new RpcPeerOptions
            {
                MaxPendingRequests = 1,
                RequestTimeout = TimeSpan.FromSeconds(30),
            }).Start();
        // First call occupies the single slot and parks awaiting a response.
        var first = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        // Second call cannot reserve a slot (pendingCount would exceed 1) -> ServiceException.
        var ex = await Assert.ThrowsAsync<ServiceException>(
            () => peer.InvokeAsync<int, string>(Service, Method, request: 2).WaitAsync(PeerOutboundTimeout));
        Assert.Contains("Maximum pending requests", ex.Message);
        // Complete the first so disposal does not have to fault it.
        channel.Enqueue(ResponseFrame(serializer, messageId: 1, result: "done"));
        Assert.Equal("done", await first.WaitAsync(PeerOutboundTimeout));
    }
}

/// <summary>
/// Test-only framing helper that builds a syntactically valid frame (correct header, envelope-length
/// prefix, exact total length) whose envelope bytes are deliberately not a valid RpcResponse. Used to
/// drive the "malformed response envelope" fault path through the read loop. Lives in the test
/// assembly only; it reuses the public <see cref="MessageFramer"/> header constants.
/// </summary>
internal static class MessageFramerTestExtensions
{
    public static Payload FrameToPayloadWithGarbageEnvelope(int messageId, byte[] garbageEnvelope) =>
        Build(messageId, garbageEnvelope);

    private static Payload Build(int messageId, byte[] garbageEnvelope)
    {
        var totalLength = MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize + garbageEnvelope.Length;
        var frame = Payload.Rent(totalLength);
        var span = frame.Memory.Span;

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)MessageType.Response;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            span.Slice(MessageFramer.HeaderSize, MessageFramer.EnvelopeLengthSize),
            garbageEnvelope.Length);
        garbageEnvelope.CopyTo(span.Slice(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize));

        return frame;
    }

}
