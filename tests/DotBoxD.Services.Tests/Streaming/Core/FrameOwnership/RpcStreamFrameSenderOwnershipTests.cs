using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamFrameSenderOwnershipTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Owned_delegate_keeps_frame_until_its_pending_send_completes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var memorySendCalled = false;
        var sender = new RpcStreamFrameSender(
            (_, _) =>
            {
                memorySendCalled = true;
                return Task.CompletedTask;
            },
            SendFrameAsync);
        var frame = CreateFrame();

        var send = sender.SendAsync(frame, CancellationToken.None);

        Assert.False(send.IsCompleted);
        _ = frame.WrittenMemory;
        release.TrySetResult();
        await send.AsTask().WaitAsync(Timeout);
        AssertDisposed(frame);
        Assert.False(memorySendCalled);

        async ValueTask SendFrameAsync(PooledBufferWriter ownedFrame, CancellationToken _)
        {
            try
            {
                await release.Task;
            }
            finally
            {
                ownedFrame.Dispose();
            }
        }
    }

    [Fact]
    public async Task Synchronous_owned_delegate_failure_is_returned_and_disposes_frame()
    {
        var expected = new IOException("simulated synchronous send failure");
        var sender = new RpcStreamFrameSender(
            static (_, _) => Task.CompletedTask,
            (_, _) => throw expected);
        var frame = CreateFrame();
        ValueTask send = default;

        var synchronousFailure = Record.Exception(
            () => send = sender.SendAsync(frame, CancellationToken.None));

        Assert.Null(synchronousFailure);
        var thrown = await Assert.ThrowsAsync<IOException>(() => send.AsTask());
        Assert.Same(expected, thrown);
        AssertDisposed(frame);
    }

    [Fact]
    public async Task Synchronous_owned_delegate_cancellation_returns_a_canceled_task()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sender = new RpcStreamFrameSender(
            static (_, _) => Task.CompletedTask,
            static (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return default;
            });
        var frame = CreateFrame();
        ValueTask send = default;

        var synchronousFailure = Record.Exception(
            () => send = sender.SendAsync(frame, cancellation.Token));

        Assert.Null(synchronousFailure);
        var task = send.AsTask();
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Equal(cancellation.Token, thrown.CancellationToken);
        AssertDisposed(frame);
    }

    [Fact]
    public async Task Pending_owned_delegate_failure_keeps_ownership_until_failure()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new IOException("simulated pending send failure");
        var sender = new RpcStreamFrameSender(
            static (_, _) => Task.CompletedTask,
            SendFrameAsync);
        var frame = CreateFrame();
        var send = sender.SendAsync(frame, CancellationToken.None);

        Assert.False(send.IsCompleted);
        _ = frame.WrittenMemory;
        release.TrySetResult();

        var thrown = await Assert.ThrowsAsync<IOException>(() => send.AsTask());
        Assert.Same(expected, thrown);
        AssertDisposed(frame);

        async ValueTask SendFrameAsync(PooledBufferWriter ownedFrame, CancellationToken _)
        {
            try
            {
                await release.Task;
                throw expected;
            }
            finally
            {
                ownedFrame.Dispose();
            }
        }
    }

    [Fact]
    public async Task Pending_owned_delegate_cancellation_keeps_ownership_until_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new RpcStreamFrameSender(
            static (_, _) => Task.CompletedTask,
            SendFrameAsync);
        var frame = CreateFrame();
        var send = sender.SendAsync(frame, cancellation.Token);
        var task = send.AsTask();

        Assert.False(task.IsCompleted);
        _ = frame.WrittenMemory;
        cancellation.Cancel();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Equal(cancellation.Token, thrown.CancellationToken);
        AssertDisposed(frame);

        static async ValueTask SendFrameAsync(
            PooledBufferWriter ownedFrame,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                ownedFrame.Dispose();
            }
        }
    }

    [Fact]
    public async Task Null_frame_failure_is_returned_instead_of_thrown_synchronously()
    {
        var sender = new RpcStreamFrameSender(
            static (_, _) => Task.CompletedTask,
            static (_, _) => default);
        ValueTask send = default;

        var synchronousFailure = Record.Exception(
            () => send = sender.SendAsync(null!, CancellationToken.None));

        Assert.Null(synchronousFailure);
        await Assert.ThrowsAsync<NullReferenceException>(() => send.AsTask());
    }

    [Fact]
    public async Task Malformed_frame_failure_is_returned_without_calling_delegate()
    {
        var delegateCalled = false;
        var sender = new RpcStreamFrameSender(
            static (_, _) => Task.CompletedTask,
            (_, _) =>
            {
                delegateCalled = true;
                return default;
            });
        var frame = CreateMalformedFrame();
        ValueTask send = default;

        var synchronousFailure = Record.Exception(
            () => send = sender.SendAsync(frame, CancellationToken.None));

        Assert.Null(synchronousFailure);
        await Assert.ThrowsAsync<InvalidDataException>(() => send.AsTask());
        Assert.False(delegateCalled);
        AssertDisposed(frame);
    }

    private static PooledBufferWriter CreateFrame()
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.StreamComplete, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static PooledBufferWriter CreateMalformedFrame()
    {
        var frame = CreateFrame();
        frame.WrittenSpan[0]++;
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}
