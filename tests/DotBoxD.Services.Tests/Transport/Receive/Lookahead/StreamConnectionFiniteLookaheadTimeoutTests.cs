using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Lookahead;

public sealed class StreamConnectionFiniteLookaheadTimeoutTests
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ReceiveAsync_CarriedFrameCleanup_DoesNotSuppressNextReadTimeout()
    {
        var first = CreateFrame(1, 3, 5, 7);
        var second = CreateFrame(2, 11, 13, 17);
        var stream = new ScriptedLookaheadReadStream(
            Join(first, second),
            Array.Empty<int>(),
            gatedReadIndex: 2);
        await using var connection = new StreamConnection(
            stream,
            ownsStream: true,
            frameReadIdleTimeout: IdleTimeout);

        using var firstReceived = await connection.ReceiveAsync().WaitAsync(Guard);
        var readCallsAfterFirst = stream.ReadCalls;

        await Task.Delay(IdleTimeout + IdleTimeout);
        using var secondReceived = await connection.ReceiveAsync().WaitAsync(Guard);

        Assert.Equal(first, firstReceived.Memory.ToArray());
        Assert.Equal(second, secondReceived.Memory.ToArray());
        Assert.Equal(2, readCallsAfterFirst);
        Assert.Equal(readCallsAfterFirst, stream.ReadCalls);

        await Assert.ThrowsAsync<IOException>(
            () => connection.ReceiveAsync().WaitAsync(Guard));
    }

    private static byte[] CreateFrame(int messageId, params byte[] body)
    {
        using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        return frame.Memory.ToArray();
    }

    private static byte[] Join(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }
}
