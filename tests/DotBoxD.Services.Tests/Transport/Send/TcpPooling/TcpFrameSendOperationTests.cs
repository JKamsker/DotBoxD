using System.Threading.Tasks.Sources;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Tests.Transport.Send.Pooling;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpFrameSendOperationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static readonly AsyncLocal<string?> Ambient = new();

    [Fact]
    public async Task PendingWrite_PublishesAfterGateReleaseAndFrameReturn()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        var frame = TcpSendTestFrames.CreatePooled(messageId: 611);
        var leaseToken = frame.LeaseToken;
        var source = new ControlledPendingSend();
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            CancellationToken.None,
            source.Pending);
        var continuationObservedCleanup = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        send.GetAwaiter().UnsafeOnCompleted(() =>
        {
            continuationObservedCleanup.TrySetResult(
                pair.Connection.SendGate.CurrentCount == 1 &&
                Record.Exception(() => _ = frame.GetWrittenMemory(leaseToken)) is
                    ObjectDisposedException);
        });

        source.Succeed();

        Assert.True(await continuationObservedCleanup.Task.WaitAsync(Guard));
        await send;
        Assert.Equal(1, source.GetResultCount);
        var successor = PooledBufferWriter.Rent();
        Assert.Same(frame, successor);
        Assert.NotEqual(leaseToken, successor.LeaseToken);
        successor.Dispose();
    }

    [Fact]
    public async Task PendingWriteFault_ReleasesGateAndFrameBeforeFailureIsObserved()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        var frame = TcpSendTestFrames.CreateOwned(messageId: 612);
        var source = new ControlledPendingSend();
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            CancellationToken.None,
            source.Pending).AsTask();

        source.Fail(new IOException("controlled write failure"));

        var error = await Assert.ThrowsAsync<IOException>(() => send.WaitAsync(Guard));
        Assert.Equal("controlled write failure", error.Message);
        Assert.Equal(1, source.GetResultCount);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        TcpSendTestFrames.AssertDisposed(frame);
    }

    [Fact]
    public async Task PendingWriteCancellation_PreservesCancellationAndCleansOwnership()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        using var cancellation = new CancellationTokenSource();
        var frame = TcpSendTestFrames.CreateOwned(messageId: 613);
        var source = new ControlledPendingSend();
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            cancellation.Token,
            source.Pending).AsTask();

        cancellation.Cancel();
        source.Fail(new OperationCanceledException(cancellation.Token));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => send.WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(send.IsCanceled);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        TcpSendTestFrames.AssertDisposed(frame);
    }

    [Fact]
    public async Task PendingWriteRegistrationFailure_CleansOwnershipAndReusesOperation()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        var frame = TcpSendTestFrames.CreateOwned(messageId: 615);
        var marker = new InvalidOperationException("controlled registration failure");
        var failingSource = new ThrowingRegistrationSend(marker);

        var failed = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            CancellationToken.None,
            failingSource.Pending).AsTask();

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => failed.WaitAsync(Guard));
        Assert.Same(marker, observed);
        TcpSendTestFrames.AssertDisposed(frame);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);

        var retainedBeforeReuse = TcpFrameSendOperation.RetainedCountForTests;
        Assert.True(retainedBeforeReuse > 0);
        Assert.True(pair.Connection.SendGate.Wait(0));
        var successorFrame = TcpSendTestFrames.CreateOwned(messageId: 616);
        var successorSource = new ControlledPendingSend();
        var successor = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            successorFrame,
            CancellationToken.None,
            successorSource.Pending);
        Assert.Equal(retainedBeforeReuse - 1, TcpFrameSendOperation.RetainedCountForTests);

        successorSource.Succeed();
        await successor.AsTask().WaitAsync(Guard);

        Assert.Equal(1, successorSource.GetResultCount);
        Assert.Equal(retainedBeforeReuse, TcpFrameSendOperation.RetainedCountForTests);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        TcpSendTestFrames.AssertDisposed(successorFrame);
    }

    [Fact]
    public async Task PendingWrite_ResumesInsideCapturedExecutionContext()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        var frame = TcpSendTestFrames.CreateOwned(messageId: 614);
        var source = new ContextObservingPendingSend();
        Ambient.Value = "captured";
        try
        {
            var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
                pair.Connection,
                frame,
                CancellationToken.None,
                source.Pending).AsTask();
            Ambient.Value = "caller-changed";

            await Task.Run(source.Succeed);
            await send.WaitAsync(Guard);

            Assert.Equal("captured", source.ObservedContext);
            Assert.Equal("caller-changed", Ambient.Value);
        }
        finally
        {
            Ambient.Value = null;
        }
    }

    private sealed class ContextObservingPendingSend : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _source;

        public ValueTask Pending => new(this, _source.Version);

        public string? ObservedContext { get; private set; }

        public void Succeed() => _source.SetResult(true);

        public void GetResult(short token)
        {
            ObservedContext = Ambient.Value;
            _source.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
    }
}
