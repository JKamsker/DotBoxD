using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Lookahead;

public sealed class StreamConnectionLookaheadRegressionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ReceiveAsync_TwoFramesInBodyLookahead_DoesNotReadForSecondFrame()
    {
        var first = CreateFrame(1, 3, 5, 7);
        var second = CreateFrame(2, 11, 13, 17);
        var stream = new ScriptedLookaheadReadStream(Join(first, second));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        var readCallsAfterFirst = stream.ReadCalls;
        using var secondReceived = await connection.ReceiveAsync();

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(2, readCallsAfterFirst);
        Assert.Equal(2, stream.ReadCalls);
        Assert.Equal(sizeof(int), stream.RequestedReadLengths[0]);
        Assert.Equal(StreamFrameReceiveBuffer.LookaheadCapacity, stream.RequestedReadLengths[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReceiveAsync_CompactsCarriedLengthPrefix(int carriedPrefixBytes)
    {
        var first = CreateFrame(10, 1, 2, 3, 4);
        var second = CreateFrame(20, 5, 6, 7, 8);
        var stream = new ScriptedLookaheadReadStream(
            Join(first, second),
            sizeof(int),
            first.Length - sizeof(int) + carriedPrefixBytes);
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        using var secondReceived = await connection.ReceiveAsync();

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(3, stream.ReadCalls);
        Assert.Equal(
            StreamFrameReceiveBuffer.LookaheadCapacity - carriedPrefixBytes,
            stream.RequestedReadLengths[2]);
    }

    [Fact]
    public async Task ReceiveAsync_LargeFrameReadsBodyAtExactFrameBoundary()
    {
        var large = CreateFrame(30, CreateBody(StreamFrameReceiveBuffer.LookaheadCapacity + 257));
        var following = CreateFrame(31, 23, 29, 31);
        var stream = new ScriptedLookaheadReadStream(Join(large, following));
        await using var connection = CreateConnection(stream);

        using var largeReceived = await connection.ReceiveAsync();

        Assert.Equal(large, largeReceived.Memory.ToArray());
        Assert.Equal(2, stream.ReadCalls);
        Assert.Equal(large.Length, stream.BytesConsumed);
        Assert.Equal(sizeof(int), stream.RequestedReadLengths[0]);
        Assert.Equal(
            large.Length - sizeof(int),
            stream.RequestedReadLengths[1]);

        using var followingReceived = await connection.ReceiveAsync();

        Assert.Equal(following, followingReceived.Memory.ToArray());
        Assert.Equal(4, stream.ReadCalls);
    }

    [Fact]
    public async Task ReceiveAsync_PreCanceledBufferedReceive_DoesNotConsumeFrame()
    {
        var first = CreateFrame(40, 37, 41);
        var second = CreateFrame(41, 43, 47);
        var stream = new ScriptedLookaheadReadStream(Join(first, second));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.ReceiveAsync(cancellation.Token));
        Assert.Equal(2, stream.ReadCalls);

        using var secondReceived = await connection.ReceiveAsync();

        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(2, stream.ReadCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReceiveAsync_CancellationWhileRefillingCarry_PreservesPrefix(int prefixBytes)
    {
        var first = CreateFrame(42, 3, 5, 7);
        var second = CreateFrame(43, 11, 13, 17);
        var stream = new ScriptedLookaheadReadStream(
            Join(first, second),
            new[] { sizeof(int), first.Length - sizeof(int) + prefixBytes },
            gatedReadIndex: 2);
        await using var connection = CreateConnection(stream);
        using var firstReceived = await connection.ReceiveAsync();
        using var cancellation = new CancellationTokenSource();
        var canceledReceive = connection.ReceiveAsync(cancellation.Token);

        await stream.WaitForGatedReadAsync(Guard);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledReceive.WaitAsync(Guard));

        stream.ReleaseGatedRead();
        using var secondReceived = await connection.ReceiveAsync().WaitAsync(Guard);

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(4, stream.ReadCalls);
    }

    [Fact]
    public async Task ReceiveAsync_AfterDrainingBufferedFrames_ReturnsCleanEof()
    {
        var first = CreateFrame(50, 53, 59);
        var second = CreateFrame(51, 61, 67);
        var stream = new ScriptedLookaheadReadStream(Join(first, second));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        using var secondReceived = await connection.ReceiveAsync();
        using var eof = await connection.ReceiveAsync();

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Same(Payload.Empty, eof);
        Assert.Equal(3, stream.ReadCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReceiveAsync_BufferedPartialPrefixThenEof_ReportsProgress(int prefixBytes)
    {
        var first = CreateFrame(55, 3, 5, 7);
        var partialPrefix = CreateFrame(56, 11, 13).AsSpan(0, prefixBytes).ToArray();
        var stream = new ScriptedLookaheadReadStream(Join(first, partialPrefix));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(
            $"Connection closed after {prefixBytes} of 4 frame length bytes.",
            exception.Message);
    }

    [Fact]
    public async Task ReceiveAsync_BufferedPartialBodyThenEof_ReportsWholeFrameProgress()
    {
        var first = CreateFrame(57, 17, 19);
        var second = CreateFrame(58, 23, 29, 31, 37, 41);
        var truncatedLength = second.Length - 2;
        var stream = new ScriptedLookaheadReadStream(Join(first, second[..truncatedLength]));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(
            $"Connection closed after {truncatedLength} of {second.Length} frame bytes.",
            exception.Message);
    }

    [Fact]
    public async Task ReceiveAsync_InvalidBufferedPrefixDoesNotDiscardTrailingFrame()
    {
        var first = CreateFrame(58, 37, 41);
        var invalidPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(invalidPrefix, sizeof(int));
        var valid = CreateFrame(59, 43, 47, 53);
        var stream = new ScriptedLookaheadReadStream(Join(first, invalidPrefix, valid));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
        using var received = await connection.ReceiveAsync();

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(valid, received.Memory.ToArray());
        Assert.Equal(2, stream.ReadCalls);
    }

    [Fact]
    public async Task ReceiveAsync_UnknownOwnedStreamKeepsExactPrefixRequest()
    {
        var expected = CreateFrame(60, 61, 67, 71);
        var stream = new RecordingExactReadStream(expected);
        await using var connection = CreateConnection(stream);

        using var received = await connection.ReceiveAsync();

        Assert.Equal(expected, received.Memory.ToArray());
        Assert.Equal(sizeof(int), stream.RequestedReadLengths[0]);
    }

    [Fact]
    public async Task ReceiveAsync_NonOwnedCapableStreamKeepsExactPrefixRequest()
    {
        var expected = CreateFrame(65, 67, 71, 73);
        using var stream = new ScriptedLookaheadReadStream(expected);
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: System.Threading.Timeout.InfiniteTimeSpan);

        using var received = await connection.ReceiveAsync();

        Assert.Equal(expected, received.Memory.ToArray());
        Assert.Equal(sizeof(int), stream.RequestedReadLengths[0]);
    }

    [Fact]
    public async Task ReceiveAsync_ReturnedFrameRemainsIndependentAfterRefillAndDispose()
    {
        var first = CreateFrame(70, 71, 73, 79);
        var second = CreateFrame(71, 83, 89, 97);
        var third = CreateFrame(72, 101, 103, 107);
        var stream = new ScriptedLookaheadReadStream(
            Join(first, second, third),
            first.Length + second.Length);
        await using var connection = CreateConnection(stream);

        using var retained = await connection.ReceiveAsync();
        using var secondReceived = await connection.ReceiveAsync();
        using var thirdReceived = await connection.ReceiveAsync();

        Assert.Equal(2, stream.ReadCalls);
        await connection.DisposeAsync();

        Assert.True(stream.IsDisposed);
        Assert.Equal(first, retained.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(third, thirdReceived.Memory.ToArray());
    }

    private static StreamConnection CreateConnection(Stream stream) =>
        new(
            stream,
            ownsStream: true,
            frameReadIdleTimeout: System.Threading.Timeout.InfiniteTimeSpan);

    private static byte[] CreateBody(int length)
    {
        var body = new byte[length];
        for (var index = 0; index < body.Length; index++)
        {
            body[index] = unchecked((byte)(index * 31));
        }

        return body;
    }

    private static byte[] CreateFrame(int messageId, params byte[] body)
    {
        using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        return frame.Memory.ToArray();
    }

    private static byte[] Join(params byte[][] frames)
    {
        var result = new byte[frames.Sum(static frame => frame.Length)];
        var offset = 0;
        foreach (var frame in frames)
        {
            frame.CopyTo(result, offset);
            offset += frame.Length;
        }

        return result;
    }
}
