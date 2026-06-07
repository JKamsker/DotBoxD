using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Coverage;

/// <summary>
/// Round 11: bugs and performance issues found by review agents.
/// These tests are written RED — they expose issues in the current code and are expected
/// to fail until the corresponding fixes are applied.
/// </summary>
public sealed class Round11_TruncatedFrameAndPerfTests
{
    // ────────────────────────────────────────────────────────────────────
    // BUG: MessageFramer.ReadMessageAsync returns null (clean EOF) when
    // the stream closes mid-payload, instead of throwing InvalidDataException.
    // A truncated frame is a protocol error, not a graceful disconnect.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMessageAsync_TruncatedPayload_ThrowsInvalidDataException()
    {
        // Arrange: build a valid header declaring 100 bytes of payload, but only write 1.
        var payloadLength = 100;
        var totalLength = MessageFramer.HeaderSize + payloadLength;
        var buffer = new byte[MessageFramer.HeaderSize + 1]; // header + 1 byte of payload
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 42); // messageId
        buffer[8] = (byte)MessageType.Request;
        buffer[9] = 0xAA; // single payload byte

        using var stream = new MemoryStream(buffer);

        // Act & Assert: should throw because the frame is incomplete, not return null.
        await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task ReadMessageAsync_TruncatedHeader_ReturnsNull()
    {
        // A partial header (fewer than 9 bytes) with no prior complete frame is a clean EOF.
        // This is the legitimate case — the peer closed before sending anything meaningful.
        var buffer = new byte[5]; // partial header
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), MessageFramer.HeaderSize + 10);
        buffer[4] = 0x01;

        using var stream = new MemoryStream(buffer);

        var result = await MessageFramer.ReadMessageAsync(stream);

        Assert.Null(result);
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG: StreamConnection.ReceiveAsync returns Payload.Empty (clean EOF)
    // when the stream closes mid-frame body, instead of throwing.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamConnection_ReceiveAsync_TruncatedBody_ThrowsInvalidDataException()
    {
        // Arrange: length prefix says 20 bytes total, but only 10 bytes follow (6 of body).
        var totalLength = 20;
        var buffer = new byte[10]; // 4 (length) + 6 (partial remaining, need 16)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), totalLength);
        // Fill with valid header bytes after length prefix
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 99); // partial messageId area
        buffer[8] = (byte)MessageType.Request;
        buffer[9] = 0xBB;

        await using var connection = new StreamConnection(new MemoryStream(buffer));

        // Act & Assert: truncated body should be a protocol error, not a clean EOF.
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
    }

    // ────────────────────────────────────────────────────────────────────
    // BUG: RpcPipeBridge.PumpAsync does not call writer.CompleteAsync()
    // when flush.IsCompleted — the pipe reader never gets an end signal.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PipeBridge_WhenReaderCompleted_WriterIsAlsoCompleted()
    {
        // Arrange
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);
        var handle = new RpcStreamHandle(300, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        var pipe = RpcPipeBridge.CreateReadablePipe(receiver, CancellationToken.None);

        // Complete the reader side first, then feed a chunk so the pump sees IsCompleted on flush.
        await pipe.Reader.CompleteAsync();

        using var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0x01, 0x02, 0x03 });
        streams.TryAcceptItem(handle.StreamId, frame);

        // Act: wait for the pump to process the chunk and hit the IsCompleted path.
        // The pump should call writer.CompleteAsync() so that FlushAsync on the writer
        // throws InvalidOperationException (writer already completed).
        await Task.Delay(500);

        // Assert: the writer should have been completed by the pump.
        // If it wasn't, FlushAsync will succeed instead of throwing.
        var writerCompleted = false;
        try
        {
            var memory = pipe.Writer.GetMemory(1);
            pipe.Writer.Advance(1);
            await pipe.Writer.FlushAsync();
        }
        catch (InvalidOperationException)
        {
            writerCompleted = true;
        }

        Assert.True(writerCompleted,
            "PipeWriter should have been completed by the pump when reader was completed, " +
            "but it was still writable.");
    }

    // ────────────────────────────────────────────────────────────────────
    // PERF: StreamConnection.ReceiveAsync rents a 4-byte buffer from
    // ArrayPool on every frame receive. This should be zero-allocation.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamConnection_ReceiveAsync_DoesNotAllocatePerFrame()
    {
        // Arrange: create a stream with 50 valid frames back to back.
        const int frameCount = 50;
        using var ms = new MemoryStream();
        var payload = new byte[] { 1, 2, 3 };
        for (var i = 0; i < frameCount; i++)
        {
            using var frame = MessageFramer.FrameToPayload(i, MessageType.Request, payload);
            ms.Write(frame.Memory.Span);
        }

        ms.Position = 0;
        await using var connection = new StreamConnection(ms, ownsStream: false);

        // Warm up: read a few frames to let tiered JIT settle.
        for (var i = 0; i < 5; i++)
        {
            using var f = await connection.ReceiveAsync();
        }

        // Measure: the remaining frames should cause zero allocations from the connection
        // itself (the Payload.Rent allocation is expected; the 4-byte ArrayPool rent is not).
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 5; i < frameCount; i++)
        {
            using var f = await connection.ReceiveAsync();
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        // Each frame currently allocates: Payload.Rent (expected) + ArrayPool.Rent(4) (unexpected).
        // The Payload.Rent returns a Payload object (~32 bytes) plus possibly the array itself.
        // The 4-byte ArrayPool rent returns at least a 16-byte array.
        // With 45 frames, the unexpected allocation is at least 45 * 16 = 720 bytes.
        // We allow up to the expected Payload allocations only.
        // Expected per frame: ~1 Payload object (24-32 bytes) + 1 rented array for the frame body.
        // We'll measure the per-frame allocation budget. With the fix, the 4-byte rent is eliminated.
        var perFrame = (after - before) / (frameCount - 5.0);

        // The current code allocates a Payload + its backing array + the 4-byte length buffer.
        // After the fix, the 4-byte length buffer allocation should be gone.
        // A Payload.Rent for a 12-byte frame body rents a 16-byte array + ~24 byte Payload object ≈ 40 bytes.
        // The extra ArrayPool rent adds at least 16 bytes + GC overhead.
        // We assert that per-frame allocation is under 80 bytes (generous for Payload-only).
        Assert.True(
            perFrame < 80,
            $"ReceiveAsync allocated {perFrame:F0} bytes per frame; expected < 80 (no extra ArrayPool rent). " +
            $"Total: {after - before} bytes for {frameCount - 5} frames.");
    }

    // ────────────────────────────────────────────────────────────────────
    // PERF: MessageFramer.ReadMessageAsync rents a header buffer from
    // ArrayPool on every call. Should use a fixed buffer instead.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMessageAsync_DoesNotAllocateHeaderBuffer()
    {
        // Arrange: create a stream with 50 valid framed messages.
        const int frameCount = 50;
        using var ms = new MemoryStream();
        var payload = new byte[] { 1, 2, 3 };
        for (var i = 0; i < frameCount; i++)
        {
            using var frame = MessageFramer.FrameToPayload(i, MessageType.Request, payload);
            ms.Write(frame.Memory.Span);
        }

        ms.Position = 0;

        // Warm up
        for (var i = 0; i < 5; i++)
        {
            var msg = await MessageFramer.ReadMessageAsync(ms);
            Assert.NotNull(msg);
            msg.Value.Body.Dispose();
        }

        // Measure
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 5; i < frameCount; i++)
        {
            var msg = await MessageFramer.ReadMessageAsync(ms);
            Assert.NotNull(msg);
            msg.Value.Body.Dispose();
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        var perCall = (after - before) / (frameCount - 5.0);

        // Each call currently allocates: Payload for body + ArrayPool header rent.
        // The Payload.Rent is expected; the header rent is not.
        // With fix: only Payload.Rent per call.
        Assert.True(
            perCall < 80,
            $"ReadMessageAsync allocated {perCall:F0} bytes per call; expected < 80 (no header buffer rent). " +
            $"Total: {after - before} bytes for {frameCount - 5} calls.");
    }
}
