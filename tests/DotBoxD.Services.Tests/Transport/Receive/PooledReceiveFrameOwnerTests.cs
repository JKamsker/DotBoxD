using System.Buffers.Binary;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Protocol.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive;

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class PooledReceiveFrameOwnerTests
{
    [Fact]
    public async Task ReceiveFrameValueAsync_StaleCopyCannotAffectFollowingFrame()
    {
        var bytes = CreateFrames(2);
        await using var connection = CreateConnection(bytes);

        var first = ReceiveSynchronously(connection);
        var stale = first;
        Assert.Equal(0, ReadMessageId(first));
        first.Dispose();

        using var second = ReceiveSynchronously(connection);
        Assert.Equal(1, ReadMessageId(second));
        Assert.Throws<ObjectDisposedException>(() => stale.Memory);
        Assert.Throws<ObjectDisposedException>(() => stale.DetachPayload());
        stale.Dispose();
    }

    [Fact]
    public async Task ReceiveFrameValueAsync_SynchronousSteadyStateDoesNotAllocate()
    {
        const int measuredFrames = 1_000;
        var bytes = CreateFrames(measuredFrames + 1);
        await using var connection = CreateConnection(bytes);

        ReceiveSynchronously(connection).Dispose();

        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < measuredFrames; i++)
        {
            var frame = ReceiveSynchronously(connection);
            checksum += ReadMessageId(frame);
            frame.Dispose();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(500_500, checksum);
        Assert.Equal(0, allocated);
    }

    private static StreamConnection CreateConnection(byte[] bytes) =>
        new(
            new MemoryStream(bytes, writable: false),
            ownsStream: false,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);

    private static RpcFrame ReceiveSynchronously(StreamConnection connection)
    {
        var pending = connection.ReceiveFrameValueAsync();
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Memory-backed receive did not complete synchronously.");
        }

        return pending.Result;
    }

    private static int ReadMessageId(RpcFrame frame)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out _));
        return messageId;
    }

    private static byte[] CreateFrames(int count)
    {
        var bytes = new byte[checked(count * MessageFramer.HeaderSize)];
        for (var i = 0; i < count; i++)
        {
            var frame = bytes.AsSpan(i * MessageFramer.HeaderSize, MessageFramer.HeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(frame, MessageFramer.HeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(frame[sizeof(int)..], i);
            frame[2 * sizeof(int)] = (byte)MessageType.Cancel;
        }

        return bytes;
    }
}
