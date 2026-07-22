using System.Buffers.Binary;
using System.Reflection;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.LookaheadLifecycle;

public sealed class StreamConnectionLookaheadLifecycleTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    private static readonly FieldInfo ReceiveBufferField =
        typeof(StreamConnection).GetField(
            "_receiveBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("StreamConnection._receiveBuffer was not found.");

    [Fact]
    public async Task DisposeAsync_WithUnreadCarry_ReleasesBufferOnceAndPreservesReturnedFrame()
    {
        var first = CreateFrame(messageId: 101, marker: 0x2A);
        var second = CreateFrame(messageId: 102, marker: 0x7D);
        var stream = new GatedLookaheadStream(Join(first, second));
        await using var connection = CreateConnection(stream);

        using var received = await connection.ReceiveAsync();

        Assert.Equal(first, received.Memory.ToArray());
        Assert.Equal(2, stream.ReadCalls);
        Assert.Equal(second.Length, GetReceiveBuffer(connection).Count);
        Assert.True(GetReceiveBuffer(connection).HasBuffer);

        await Task.WhenAll(
            connection.DisposeAsync().AsTask(),
            connection.DisposeAsync().AsTask()).WaitAsync(Guard);

        Assert.False(GetReceiveBuffer(connection).HasBuffer);
        Assert.Equal(first, received.Memory.ToArray());
        Assert.Equal(1, stream.DisposeCalls);

        await AssertFutureReceiveIsDisposedAsync(connection, stream);
        await connection.DisposeAsync();

        Assert.Equal(1, stream.DisposeCalls);
    }

    [Fact]
    public async Task DisposeAsync_DuringPendingRead_KeepsBufferUntilReceiveExits()
    {
        var expected = CreateFrame(messageId: 201, marker: 0x5C);
        var stream = new GatedLookaheadStream(
            expected,
            gatedReadCall: 2,
            gateDispose: true);
        await using var connection = CreateConnection(stream);
        var receive = connection.ReceiveAsync();
        try
        {
            await stream.WaitForReadAsync(Guard);
            Assert.True(GetReceiveBuffer(connection).HasBuffer);

            var dispose = connection.DisposeAsync().AsTask();
            await stream.WaitForDisposeAsync(Guard);

            Assert.True(GetReceiveBuffer(connection).HasBuffer);
            stream.ReleaseRead();

            using var received = await receive.WaitAsync(Guard);

            Assert.False(GetReceiveBuffer(connection).HasBuffer);
            Assert.Equal(expected, received.Memory.ToArray());

            stream.ReleaseDispose();
            await dispose.WaitAsync(Guard);

            Assert.Equal(expected, received.Memory.ToArray());
            Assert.Equal(2, stream.ReadCalls);
            Assert.Equal(1, stream.DisposeCalls);

            await AssertFutureReceiveIsDisposedAsync(connection, stream);
        }
        finally
        {
            stream.ReleaseRead();
            stream.ReleaseDispose();
        }
    }

    private static StreamConnection CreateConnection(Stream stream) =>
        new(
            stream,
            ownsStream: true,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);

    private static async Task AssertFutureReceiveIsDisposedAsync(
        StreamConnection connection,
        GatedLookaheadStream stream)
    {
        var readCalls = stream.ReadCalls;

        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.ReceiveAsync());

        Assert.Equal(readCalls, stream.ReadCalls);
        Assert.False(GetReceiveBuffer(connection).HasBuffer);
    }

    private static StreamFrameReceiveBuffer GetReceiveBuffer(StreamConnection connection) =>
        (StreamFrameReceiveBuffer)(ReceiveBufferField.GetValue(connection)
            ?? throw new InvalidOperationException("StreamConnection._receiveBuffer is null."));

    private static byte[] CreateFrame(int messageId, byte marker)
    {
        var frame = new byte[MessageFramer.HeaderSize + 1];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(sizeof(int)), messageId);
        frame[8] = (byte)MessageType.Response;
        frame[^1] = marker;
        return frame;
    }

    private static byte[] Join(byte[] first, byte[] second)
    {
        var joined = new byte[first.Length + second.Length];
        first.CopyTo(joined, 0);
        second.CopyTo(joined, first.Length);
        return joined;
    }
}
