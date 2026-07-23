using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

public sealed class TcpFrameReceiveOperationOrderingTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task DisposedConnectionWinsOverPreCancellation()
    {
        await using var pair = await TcpReceiveTestPair.CreateAsync();
        await pair.Connection.DisposeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.ReceiveFrameValueAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task LivePreCancellationDoesNotConsumeQueuedFrameAndReleasesSlot()
    {
        await using var pair = await TcpReceiveTestPair.CreateAsync();
        var expected = TcpReceiveTestPair.CreateFrame(messageId: 411);
        await pair.QueueBytesAsync(expected);
        await pair.WaitForQueuedBytesAsync(expected.Length);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pair.Connection.ReceiveFrameValueAsync(cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);

        using var frame = await pair.Connection.ReceiveFrameValueAsync().AsTask().WaitAsync(Guard);
        AssertFrame(frame, messageId: 411);
    }

    [Fact]
    public async Task ActiveReceiveWinsOverPreCanceledSuccessor()
    {
        await using var pair = await TcpReceiveTestPair.CreateAsync();
        using var firstCancellation = new CancellationTokenSource();
        var first = pair.Connection.ReceiveFrameValueAsync(firstCancellation.Token);
        using var secondCancellation = new CancellationTokenSource();
        secondCancellation.Cancel();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pair.Connection.ReceiveFrameValueAsync(secondCancellation.Token).AsTask());

        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => first.AsTask().WaitAsync(Guard));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CallerCancellationAtPrefixOrBodyReleasesReceiveSlot(
        bool body,
        bool finiteIdleTimeout)
    {
        await using var pair = await TcpReceiveTestPair.CreateAsync(
            finiteIdleTimeout ? TimeSpan.FromSeconds(30) : null);
        var interrupted = TcpReceiveTestPair.CreateFrame(messageId: 412);
        using var cancellation = new CancellationTokenSource();
        var pending = pair.Connection.ReceiveFrameValueAsync(cancellation.Token);
        if (body)
        {
            await pair.QueueBytesAsync(interrupted.AsMemory(0, sizeof(int)));
            await pair.WaitForPrefixAsync(interrupted.Length);
        }

        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.AsTask().WaitAsync(Guard));
        if (!finiteIdleTimeout)
        {
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }

        var successor = TcpReceiveTestPair.CreateFrame(messageId: 413);
        var next = pair.Connection.ReceiveFrameValueAsync();
        await pair.QueueBytesAsync(successor);
        using var frame = await next.AsTask().WaitAsync(Guard);
        AssertFrame(frame, messageId: 413);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleTimeoutAtPrefixOrBodyReleasesReceiveSlot(bool body)
    {
        await using var pair = await TcpReceiveTestPair.CreateAsync(IdleTimeout);
        byte[]? interrupted = null;
        if (body)
        {
            interrupted = TcpReceiveTestPair.CreateFrame(messageId: 414);
            await pair.QueueBytesAsync(interrupted.AsMemory(0, sizeof(int)));
            await pair.WaitForQueuedBytesAsync(sizeof(int));
        }

        var pending = pair.Connection.ReceiveFrameValueAsync();
        if (body)
        {
            await pair.WaitForPrefixAsync(interrupted!.Length);
        }

        var timeout = await Assert.ThrowsAsync<IOException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Contains("stalled", timeout.Message);

        var successor = TcpReceiveTestPair.CreateFrame(messageId: 415);
        var next = pair.Connection.ReceiveFrameValueAsync();
        await pair.QueueBytesAsync(successor);
        using var frame = await next.AsTask().WaitAsync(Guard);
        AssertFrame(frame, messageId: 415);
    }

    private static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }
}
