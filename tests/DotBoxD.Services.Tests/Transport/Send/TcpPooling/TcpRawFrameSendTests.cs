using System.Buffers;
using DotBoxD.Services.Tests.Transport.Send.Pooling;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpRawFrameSendTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task UncontendedRawSend_PreservesCallerOwnershipForDefaultAndLiveTokens()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 619);
        using var owner = new TrackingMemoryOwner(expected);
        using var cancellation = new CancellationTokenSource();

        var defaultSend = pair.Connection.SendValueAsync(owner.Memory);
        await defaultSend.AsTask().WaitAsync(Guard);
        Assert.Equal(expected, await pair.ReadAsync(expected.Length));

        var liveSend = pair.Connection.SendValueAsync(owner.Memory, cancellation.Token);
        await liveSend.AsTask().WaitAsync(Guard);
        Assert.Equal(expected, await pair.ReadAsync(expected.Length));
        AssertCallerOwnership(owner, expected);
    }

    [Fact]
    public async Task HeldGate_RawMemoryRemainsCallerOwnedAndOperationRecycles()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 620);
        using var owner = new TrackingMemoryOwner(expected);

        await CompleteControlledPendingWriteAsync(pair, owner.Memory);
        var retained = TcpFrameSendOperation.RetainedCountForTests;
        Assert.True(retained > 0);

        Assert.True(pair.Connection.SendGate.Wait(0));
        var send = pair.Connection.SendValueAsync(owner.Memory);

        Assert.False(send.IsCompleted);
        Assert.Equal(retained - 1, TcpFrameSendOperation.RetainedCountForTests);
        AssertCallerOwnership(owner, expected);

        pair.Connection.ReleaseSendGate();
        await send.AsTask();
        // An AsTask consumer may resume inline from SetResult before the producer unwinds its
        // publication finally. Wait for that observable lifecycle transition instead of assuming
        // one scheduler yield is sufficient on every runtime and host.
        await WaitForRetainedCountAsync(retained);

        Assert.Equal(retained, TcpFrameSendOperation.RetainedCountForTests);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        AssertCallerOwnership(owner, expected);
        Assert.Equal(expected, await pair.ReadAsync(expected.Length));
    }

    [Fact]
    public async Task PendingRawWriteFault_ReleasesGateAndPreservesCallerOwnership()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 621);
        using var owner = new TrackingMemoryOwner(expected);
        Assert.True(pair.Connection.SendGate.Wait(0));
        var source = new ControlledPendingSend();
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            owner.Memory,
            CancellationToken.None,
            source.Pending).AsTask();

        source.Fail(new IOException("controlled raw write failure"));

        var error = await Assert.ThrowsAsync<IOException>(() => send.WaitAsync(Guard));
        Assert.Equal("controlled raw write failure", error.Message);
        Assert.Equal(1, source.GetResultCount);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        AssertCallerOwnership(owner, expected);
    }

    [Fact]
    public async Task PendingRawWriteRegistrationFailure_ReleasesGateAndPreservesCallerOwnership()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 624);
        using var owner = new TrackingMemoryOwner(expected);
        Assert.True(pair.Connection.SendGate.Wait(0));
        var marker = new InvalidOperationException("controlled raw registration failure");
        var source = new ThrowingRegistrationSend(marker);

        var failed = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            owner.Memory,
            CancellationToken.None,
            source.Pending).AsTask();

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => failed.WaitAsync(Guard));
        Assert.Same(marker, observed);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        AssertCallerOwnership(owner, expected);
    }

    [Fact]
    public async Task PendingRawWriteCancellation_ReleasesGateAndPreservesCallerToken()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 622);
        using var owner = new TrackingMemoryOwner(expected);
        using var cancellation = new CancellationTokenSource();
        Assert.True(pair.Connection.SendGate.Wait(0));
        var source = new ControlledPendingSend();
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            owner.Memory,
            cancellation.Token,
            source.Pending).AsTask();

        cancellation.Cancel();
        source.Fail(new OperationCanceledException(cancellation.Token));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => send.WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(send.IsCanceled);
        Assert.Equal(1, source.GetResultCount);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        AssertCallerOwnership(owner, expected);
    }

    [Fact]
    public async Task SynchronousFailures_AreCapturedWithOriginalPrecedence()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var malformed = new byte[1];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var canceled = CaptureValueSend(
            () => pair.Connection.SendValueAsync(malformed, cancellation.Token));
        var cancellationError = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceled.AsTask());
        Assert.Equal(cancellation.Token, cancellationError.CancellationToken);
        var fallbackCanceled = CaptureValueSend(
            () => TcpFrameSendFallback.StartRawFromBeginning(
                pair.Connection,
                malformed,
                cancellation.Token));
        var fallbackCancellationError = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fallbackCanceled.AsTask());
        Assert.Equal(cancellation.Token, fallbackCancellationError.CancellationToken);

        var invalid = CaptureValueSend(() => pair.Connection.SendValueAsync(malformed));
        await Assert.ThrowsAsync<InvalidDataException>(() => invalid.AsTask());
        var fallbackInvalid = CaptureValueSend(
            () => TcpFrameSendFallback.StartRawFromBeginning(
                pair.Connection,
                malformed,
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => fallbackInvalid.AsTask());

        await pair.Connection.DisposeAsync();
        var disposed = CaptureValueSend(
            () => pair.Connection.SendValueAsync(malformed, cancellation.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => disposed.AsTask());
        var fallbackDisposed = CaptureValueSend(
            () => TcpFrameSendFallback.StartRawFromBeginning(
                pair.Connection,
                malformed,
                cancellation.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fallbackDisposed.AsTask());

        var task = CaptureTaskSend(
            () => pair.Connection.SendAsync(malformed, cancellation.Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
    }

    [Fact]
    public async Task SendAsync_TaskAdapterTracksPendingRawSendWithoutTakingOwnership()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 623);
        using var owner = new TrackingMemoryOwner(expected);
        Assert.True(pair.Connection.SendGate.Wait(0));

        var send = CaptureTaskSend(() => pair.Connection.SendAsync(owner.Memory));

        Assert.False(send.IsCompleted);
        AssertCallerOwnership(owner, expected);
        pair.Connection.ReleaseSendGate();
        await send.WaitAsync(Guard);

        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        AssertCallerOwnership(owner, expected);
        Assert.Equal(expected, await pair.ReadAsync(expected.Length));
    }

    private static async Task CompleteControlledPendingWriteAsync(
        TcpSendTestPair pair,
        ReadOnlyMemory<byte> frame)
    {
        Assert.True(pair.Connection.SendGate.Wait(0));
        var source = new ControlledPendingSend();
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            CancellationToken.None,
            source.Pending);
        Assert.False(send.IsCompleted);
        source.Succeed();
        await send.AsTask().WaitAsync(Guard);
        Assert.Equal(1, source.GetResultCount);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
    }

    private static ValueTask CaptureValueSend(Func<ValueTask> send)
    {
        var pending = default(ValueTask);
        var synchronousError = Record.Exception(() => { pending = send(); });
        Assert.Null(synchronousError);
        return pending;
    }

    private static Task CaptureTaskSend(Func<Task> send)
    {
        Task? pending = null;
        var synchronousError = Record.Exception(() => { pending = send(); });
        Assert.Null(synchronousError);
        return Assert.IsAssignableFrom<Task>(pending);
    }

    private static async Task WaitForRetainedCountAsync(int expected)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (TcpFrameSendOperation.RetainedCountForTests != expected &&
               watch.Elapsed < Guard)
        {
            await Task.Delay(1);
        }
    }

    private static void AssertCallerOwnership(TrackingMemoryOwner owner, byte[] expected)
    {
        Assert.False(owner.IsDisposed);
        Assert.True(owner.Memory.Span.SequenceEqual(expected));
    }

    private sealed class TrackingMemoryOwner(byte[] bytes) : IMemoryOwner<byte>
    {
        private readonly byte[] _bytes = bytes.ToArray();

        public bool IsDisposed { get; private set; }

        public Memory<byte> Memory => IsDisposed
            ? throw new ObjectDisposedException(nameof(TrackingMemoryOwner))
            : _bytes;

        public void Dispose() => IsDisposed = true;
    }
}
