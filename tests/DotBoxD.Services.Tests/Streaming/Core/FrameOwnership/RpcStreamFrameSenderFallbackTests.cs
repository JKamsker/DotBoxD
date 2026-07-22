using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamFrameSenderFallbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Memory_fallback_keeps_frame_until_pending_send_completes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[]? sent = null;
        var sender = new RpcStreamFrameSender(SendMemoryAsync, sendFrameAsync: null);
        var frame = CreateFrame();
        var expected = frame.WrittenMemory.ToArray();

        var send = sender.SendAsync(frame, CancellationToken.None);
        await entered.Task.WaitAsync(Timeout);
        Assert.False(send.IsCompleted);
        _ = frame.WrittenMemory;

        release.TrySetResult();
        await send.AsTask().WaitAsync(Timeout);
        Assert.Equal(expected, sent);
        AssertDisposed(frame);

        async Task SendMemoryAsync(ReadOnlyMemory<byte> memory, CancellationToken _)
        {
            entered.TrySetResult();
            await release.Task;
            sent = memory.ToArray();
        }
    }

    [Fact]
    public async Task Memory_fallback_keeps_frame_until_pending_send_fails()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new IOException("simulated pending memory send failure");
        var sender = new RpcStreamFrameSender(SendMemoryAsync, sendFrameAsync: null);
        var frame = CreateFrame();

        var send = sender.SendAsync(frame, CancellationToken.None);
        await entered.Task.WaitAsync(Timeout);
        Assert.False(send.IsCompleted);
        _ = frame.WrittenMemory;

        release.TrySetResult();
        var thrown = await Assert.ThrowsAsync<IOException>(() => send.AsTask());
        Assert.Same(expected, thrown);
        AssertDisposed(frame);

        async Task SendMemoryAsync(ReadOnlyMemory<byte> _, CancellationToken __)
        {
            entered.TrySetResult();
            await release.Task;
            throw expected;
        }
    }

    [Fact]
    public async Task Memory_fallback_keeps_frame_until_pending_send_is_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RpcStreamFrameSender(SendMemoryAsync, sendFrameAsync: null);
        var frame = CreateFrame();

        var send = sender.SendAsync(frame, cancellation.Token);
        var task = send.AsTask();
        await entered.Task.WaitAsync(Timeout);
        Assert.False(task.IsCompleted);
        _ = frame.WrittenMemory;

        cancellation.Cancel();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Equal(cancellation.Token, thrown.CancellationToken);
        AssertDisposed(frame);

        async Task SendMemoryAsync(ReadOnlyMemory<byte> _, CancellationToken ct)
        {
            entered.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
        }
    }

    [Fact]
    public async Task Synchronous_memory_delegate_failure_is_returned_and_disposes_frame()
    {
        var expected = new IOException("simulated synchronous memory send failure");
        var sender = new RpcStreamFrameSender((_, _) => throw expected, sendFrameAsync: null);
        var frame = CreateFrame();
        ValueTask send = default;

        var synchronousFailure = Record.Exception(
            () => send = sender.SendAsync(frame, CancellationToken.None));

        Assert.Null(synchronousFailure);
        var thrown = await Assert.ThrowsAsync<IOException>(() => send.AsTask());
        Assert.Same(expected, thrown);
        AssertDisposed(frame);
    }

    private static PooledBufferWriter CreateFrame()
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.StreamComplete, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}
