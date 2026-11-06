using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

// Regression: the request Streams array was sized straight from its MessagePack header. The reader only
// guarantees one remaining byte per declared element, so a frame of N filler bytes could force an
// N-element RpcStreamHandle[] (8 bytes each) before handle parsing failed. Counts that the remaining
// input cannot hold as minimal handles must be rejected before allocating.
public sealed class RpcRequestStreamCountValidationTests
{
    private const int MinimumHandleSize = 17;

    [Fact]
    public void OversizedStreamCount_IsRejectedBeforeAllocatingHandles()
    {
        const int declaredCount = 2_000_000;
        var bytes = WriteRequest(static (ref MessagePackWriter writer) =>
        {
            writer.WriteArrayHeader(declaredCount);
            for (var i = 0; i < declaredCount; i++)
            {
                writer.Write(0); // one-byte filler: satisfies the reader's per-element byte check only
            }
        });
        var serializer = new MessagePackRpcSerializer();

        // Warm up the formatter path so the measured window only reflects the rejected request.
        Assert.Null(serializer.Deserialize<RpcRequest>(
            WriteRequest(static (ref MessagePackWriter writer) => writer.WriteNil())).Streams);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(bytes));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("stream handle count", error.ToString());
        Assert.True(
            allocated < declaredCount,
            $"Rejecting the request allocated {allocated:N0} bytes for {declaredCount:N0} declared handles.");
    }

    [Fact]
    public void MinimallyEncodedHandlesFillingThePayload_AreAccepted()
    {
        var bytes = WriteRequest(static (ref MessagePackWriter writer) =>
        {
            writer.WriteArrayHeader(2);
            WriteMinimalHandle(ref writer, streamId: 1, kind: RpcStreamKind.Binary);
            WriteMinimalHandle(ref writer, streamId: 2, kind: RpcStreamKind.Items);
        });

        var request = new MessagePackRpcSerializer().Deserialize<RpcRequest>(bytes);

        Assert.NotNull(request.Streams);
        Assert.Equal([1, 2], request.Streams.Select(handle => handle.StreamId));
        Assert.Equal([RpcStreamKind.Binary, RpcStreamKind.Items], request.Streams.Select(handle => handle.Kind));
    }

    private static void WriteMinimalHandle(ref MessagePackWriter writer, int streamId, RpcStreamKind kind)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var handleWriter = new MessagePackWriter(buffer);
        handleWriter.WriteMapHeader(2);
        handleWriter.Write("StreamId");
        handleWriter.Write(streamId);
        handleWriter.Write("Kind");
        handleWriter.Write((byte)kind);
        handleWriter.Flush();
        Assert.Equal(MinimumHandleSize, buffer.WrittenCount);
        writer.WriteRaw(buffer.WrittenSpan);
    }

    private delegate void StreamsWriter(ref MessagePackWriter writer);

    // Streams is written last so the remaining input after its header is exactly the handle bytes.
    private static ReadOnlyMemory<byte> WriteRequest(StreamsWriter writeStreams)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(4);
        writer.Write("MessageId");
        writer.Write(42);
        writer.Write("ServiceName");
        writer.Write("Svc");
        writer.Write("MethodName");
        writer.Write("Op");
        writer.Write("Streams");
        writeStreams(ref writer);
        writer.Flush();
        return buffer.WrittenMemory;
    }
}
