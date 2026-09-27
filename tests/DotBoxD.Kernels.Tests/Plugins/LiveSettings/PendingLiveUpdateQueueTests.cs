using System.Reflection;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PendingLiveUpdateQueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Flush_preserves_failure_from_update_enqueued_after_its_snapshot(bool firstFails)
    {
        var queue = new PendingLiveUpdateQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFailure = new InvalidOperationException("first update failed");
        var laterFailure = new InvalidOperationException("later update failed");
        var firstUpdate = Enqueue(queue, () => release.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
        Task? flush = null;

        try
        {
            if (firstFails)
            {
                var failedUpdate = Enqueue(queue, () => throw firstFailure);
                await Assert.ThrowsAsync<InvalidOperationException>(() => failedUpdate.WaitAsync(Timeout));
            }

            flush = queue.FlushAsync().AsTask();
            var laterUpdate = Enqueue(queue, () => throw laterFailure);
            var updateError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => laterUpdate.WaitAsync(Timeout));
            Assert.Same(laterFailure, updateError);

            release.SetResult();
            var firstFlushError = await Record.ExceptionAsync(() => flush.WaitAsync(Timeout));
            var retainedError = queue.LastError;
            var nextFlushError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => queue.FlushAsync().AsTask().WaitAsync(Timeout));

            Assert.Same(laterFailure, retainedError);
            Assert.Same(laterFailure, nextFlushError.InnerException);
            if (firstFails)
            {
                Assert.Same(firstFailure, Assert.IsType<InvalidOperationException>(firstFlushError).InnerException);
            }
            else
            {
                Assert.Null(firstFlushError);
            }
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => firstUpdate.WaitAsync(Timeout));
            if (flush is not null)
            {
                await Record.ExceptionAsync(() => flush.WaitAsync(Timeout));
            }
        }
    }

    [Fact]
    public async Task Canceling_flush_preserves_failed_updates_for_the_next_flush()
    {
        var queue = new PendingLiveUpdateQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedUpdate = Enqueue(queue, () => release.Task.WaitAsync(Timeout).GetAwaiter().GetResult());
        var failure = new InvalidOperationException("update failed");
        var failedUpdate = Enqueue(queue, () => throw failure);
        using var cancellation = new CancellationTokenSource();
        Task? flush = null;

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => failedUpdate.WaitAsync(Timeout));
            flush = queue.FlushAsync(cancellation.Token).AsTask();
            await cancellation.CancelAsync();

            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush.WaitAsync(Timeout));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Same(failure, queue.LastError);

            release.SetResult();
            await blockedUpdate.WaitAsync(Timeout);
            var nextFlushError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => queue.FlushAsync().AsTask().WaitAsync(Timeout));
            Assert.Same(failure, nextFlushError.InnerException);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => blockedUpdate.WaitAsync(Timeout));
            if (flush is not null)
            {
                await Record.ExceptionAsync(() => flush.WaitAsync(Timeout));
            }
        }
    }

    [Fact]
    public async Task Successful_flush_clears_previously_reported_failure()
    {
        var queue = new PendingLiveUpdateQueue();
        var failure = new InvalidOperationException("update failed");
        var failedUpdate = Enqueue(queue, () => throw failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failedUpdate.WaitAsync(Timeout));
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.FlushAsync().AsTask().WaitAsync(Timeout));

        await queue.FlushAsync().AsTask().WaitAsync(Timeout);

        Assert.Null(queue.LastError);
    }

    [Fact]
    public async Task Canceled_update_is_reported_as_a_failure_when_flush_is_not_canceled()
    {
        var queue = new PendingLiveUpdateQueue();
        var failedUpdate = Enqueue(queue, () => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => failedUpdate.WaitAsync(Timeout));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.FlushAsync().AsTask().WaitAsync(Timeout));

        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.IsAssignableFrom<OperationCanceledException>(queue.LastError);
    }

    private static Task Enqueue(PendingLiveUpdateQueue queue, Action update)
    {
        // Observe the actual fire-and-forget task so the race does not depend on timing delays.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var gate = typeof(PendingLiveUpdateQueue).GetField("_gate", flags)!.GetValue(queue)!;
        var pending = (List<Task>)typeof(PendingLiveUpdateQueue).GetField("_pending", flags)!.GetValue(queue)!;
        lock (gate)
        {
            queue.Enqueue(update);
            return pending[^1];
        }
    }
}
