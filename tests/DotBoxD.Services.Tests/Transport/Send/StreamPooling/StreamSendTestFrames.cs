using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

internal static class StreamSendTestFrames
{
    public static PooledBufferWriter Create(int messageId, out byte[] expected)
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(
            frame,
            messageId,
            MessageType.Request,
            ReadOnlySpan<byte>.Empty);
        expected = frame.WrittenMemory.ToArray();
        return frame;
    }

    public static PooledBufferWriter Rent(int messageId, out byte[] expected)
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(
            frame,
            messageId,
            MessageType.Request,
            ReadOnlySpan<byte>.Empty);
        expected = frame.WrittenMemory.ToArray();
        return frame;
    }

    public static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}
