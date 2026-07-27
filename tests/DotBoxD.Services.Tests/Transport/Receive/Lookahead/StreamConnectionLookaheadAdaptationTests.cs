using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Lookahead;

public sealed class StreamConnectionLookaheadAdaptationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ReceiveAsync_ExactBodyLookaheadMissBacksOffNextFrame()
    {
        var first = CreateFrame(3, 19, 23, 29);
        var second = CreateFrame(4, 31, 37, 41);
        var stream = new ScriptedLookaheadReadStream(
            Join(first, second),
            sizeof(int),
            first.Length - sizeof(int));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        using var secondReceived = await connection.ReceiveAsync();

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(4, stream.ReadCalls);
        Assert.Equal(StreamFrameReceiveBuffer.LookaheadCapacity, stream.RequestedReadLengths[1]);
        Assert.Equal(sizeof(int), stream.RequestedReadLengths[2]);
        Assert.Equal(second.Length - sizeof(int), stream.RequestedReadLengths[3]);
    }

    [Fact]
    public async Task ReceiveAsync_DrainedCarryKeepsNextBatchLookaheadEnabled()
    {
        var first = CreateFrame(5, 43, 47);
        var second = CreateFrame(6, 53, 59);
        var third = CreateFrame(7, 61, 67);
        var fourth = CreateFrame(8, 71, 73);
        var stream = new ScriptedLookaheadReadStream(
            Join(first, second, third, fourth),
            sizeof(int),
            first.Length - sizeof(int) + second.Length,
            sizeof(int));
        await using var connection = CreateConnection(stream);

        using var firstReceived = await connection.ReceiveAsync();
        using var secondReceived = await connection.ReceiveAsync();
        using var thirdReceived = await connection.ReceiveAsync();
        using var fourthReceived = await connection.ReceiveAsync();

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(third, thirdReceived.Memory.ToArray());
        Assert.Equal(fourth, fourthReceived.Memory.ToArray());
        Assert.Equal(4, stream.ReadCalls);
        Assert.Equal(StreamFrameReceiveBuffer.LookaheadCapacity, stream.RequestedReadLengths[1]);
        Assert.Equal(sizeof(int), stream.RequestedReadLengths[2]);
        Assert.Equal(StreamFrameReceiveBuffer.LookaheadCapacity, stream.RequestedReadLengths[3]);
    }

    [Fact]
    public async Task ReceiveAsync_PendingPartialExactPrefixKeepsBodyOnDirectPath()
    {
        var expected = CreateFrame(9, 79, 83, 89, 97);
        var stream = new ScriptedLookaheadReadStream(
            expected,
            new[] { 1, 3 },
            gatedReadIndex: 0);
        await using var connection = CreateConnection(stream);

        var receive = connection.ReceiveAsync();
        await stream.WaitForGatedReadAsync(Guard);
        stream.ReleaseGatedRead();
        using var received = await receive.WaitAsync(Guard);

        Assert.Equal(expected, received.Memory.ToArray());
        Assert.Equal(new[] { sizeof(int), 3, expected.Length - sizeof(int) },
            stream.RequestedReadLengths);
    }

    [Fact]
    public async Task ReceiveAsync_PartialBodyLookaheadThenEofReportsWholeFrameProgress()
    {
        var frame = CreateFrame(10, 101, 103, 107, 109, 113);
        var truncatedLength = frame.Length - 2;
        var stream = new ScriptedLookaheadReadStream(frame[..truncatedLength]);
        await using var connection = CreateConnection(stream);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => connection.ReceiveAsync());

        Assert.Equal(
            $"Connection closed after {truncatedLength} of {frame.Length} frame bytes.",
            exception.Message);
        Assert.Equal(
            new[]
            {
                sizeof(int),
                StreamFrameReceiveBuffer.LookaheadCapacity,
                StreamFrameReceiveBuffer.LookaheadCapacity,
            },
            stream.RequestedReadLengths);
    }

    [Fact]
    public async Task ReceiveAsync_BodyLookaheadIdleTimeoutRemainsTimeout()
    {
        var frame = CreateFrame(11, 127, 131, 137);
        var stream = new ScriptedLookaheadReadStream(
            frame,
            Array.Empty<int>(),
            gatedReadIndex: 1);
        await using var connection = new StreamConnection(
            stream,
            ownsStream: true,
            frameReadIdleTimeout: TimeSpan.FromMilliseconds(30));

        var receive = connection.ReceiveAsync();
        await stream.WaitForGatedReadAsync(Guard);
        var exception = await Assert.ThrowsAsync<IOException>(() => receive.WaitAsync(Guard));
        Assert.Contains("Inbound frame read stalled", exception.Message, StringComparison.Ordinal);
        stream.ReleaseGatedRead();
    }

    [Theory]
    [InlineData(StreamFrameReceiveBuffer.LookaheadCapacity, true)]
    [InlineData(StreamFrameReceiveBuffer.LookaheadCapacity + 1, false)]
    public async Task ReceiveAsync_LookaheadHonorsSmallFrameBoundary(
        int totalLength,
        bool expectsLookahead)
    {
        var expected = CreateFrameWithTotalLength(totalLength);
        var stream = new ScriptedLookaheadReadStream(expected);
        await using var connection = CreateConnection(stream);

        using var received = await connection.ReceiveAsync();

        Assert.Equal(expected, received.Memory.ToArray());
        Assert.Equal(
            expectsLookahead
                ? StreamFrameReceiveBuffer.LookaheadCapacity
                : totalLength - sizeof(int),
            stream.RequestedReadLengths[1]);
    }

    private static StreamConnection CreateConnection(Stream stream) =>
        new(
            stream,
            ownsStream: true,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);

    private static byte[] CreateFrame(int messageId, params byte[] body)
    {
        using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        return frame.Memory.ToArray();
    }

    private static byte[] CreateFrameWithTotalLength(int totalLength)
    {
        var body = new byte[totalLength - MessageFramer.HeaderSize];
        return CreateFrame(12, body);
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
