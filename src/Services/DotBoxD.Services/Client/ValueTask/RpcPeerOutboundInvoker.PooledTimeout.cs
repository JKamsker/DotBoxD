using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    internal void StartPooledTimeoutIfNeeded(PooledPendingResponse pending, long deadline)
    {
        if (_hasFiniteTimeout && !pending.CompletionStarted)
        {
            _pending.StartTimeout(pending, deadline);
        }
    }

    // A send still in flight under a finite RequestTimeout or a cancelable caller token races its
    // deadline and caller cancellation; any other send keeps the plain wrapper. The caller saw the send
    // incomplete, but it may have completed since, so the plain wrapper arms the deadline here for a
    // send that already succeeded, exactly as the synchronous path does. A send that already failed
    // needs no deadline: its wrapper rethrows the failure at once.
    private ValueTask<TResponse> AwaitPooledUnaryFrameSendAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        ValueTask send,
        long deadline,
        CancellationToken ct)
    {
        if (ShouldRaceSend(send.IsCompleted, ct))
        {
            return PooledUnaryPendingSend.AwaitWithinDeadlineAsync(
                this,
                messageId,
                pending,
                send.AsTask(),
                deadline,
                ct);
        }

        StartPooledTimeoutForCompletedSend(pending, send.IsCompletedSuccessfully, deadline);
        return PooledUnaryPendingSend.AwaitFrameAsync(this, messageId, pending, send, ct);
    }

    private ValueTask<TResponse> AwaitPooledUnaryMemorySendAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        PooledBufferWriter frame,
        Task send,
        long deadline,
        CancellationToken ct)
    {
        if (ShouldRaceSend(send.IsCompleted, ct))
        {
            return PooledUnaryPendingSend.AwaitWithinDeadlineAsync(
                this,
                messageId,
                pending,
                DisposeFrameAfterSendAsync(frame, send),
                deadline,
                ct);
        }

        StartPooledTimeoutForCompletedSend(pending, send.IsCompletedSuccessfully, deadline);
        return PooledUnaryPendingSend.AwaitMemoryAsync(this, messageId, pending, frame, send, ct);
    }

    private ValueTask AwaitPooledNoResponseFrameSendAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
        ValueTask send,
        long deadline,
        CancellationToken ct)
    {
        if (ShouldRaceSend(send.IsCompleted, ct))
        {
            return PooledNoResponsePendingSend.AwaitWithinDeadlineAsync(
                this,
                messageId,
                pending,
                send.AsTask(),
                deadline,
                ct);
        }

        StartPooledTimeoutForCompletedSend(pending, send.IsCompletedSuccessfully, deadline);
        return PooledNoResponsePendingSend.AwaitFrameAsync(this, messageId, pending, send, ct);
    }

    private ValueTask AwaitPooledNoResponseMemorySendAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
        PooledBufferWriter frame,
        Task send,
        long deadline,
        CancellationToken ct)
    {
        if (ShouldRaceSend(send.IsCompleted, ct))
        {
            return PooledNoResponsePendingSend.AwaitWithinDeadlineAsync(
                this,
                messageId,
                pending,
                DisposeFrameAfterSendAsync(frame, send),
                deadline,
                ct);
        }

        StartPooledTimeoutForCompletedSend(pending, send.IsCompletedSuccessfully, deadline);
        return PooledNoResponsePendingSend.AwaitMemoryAsync(this, messageId, pending, frame, send, ct);
    }

    private void StartPooledTimeoutForCompletedSend(
        PooledPendingResponse pending,
        bool sendSucceeded,
        long deadline)
    {
        if (sendSucceeded)
        {
            StartPooledTimeoutIfNeeded(pending, deadline);
        }
    }

    /// <summary>
    /// Completes a pooled wrapper that observed its source through <paramref name="response"/>
    /// (the finite-timeout send race). The Task observer is the source's single consumer, so an
    /// unfinished source is completed as abandoned for that observer to consume, and any fault the
    /// wrapper did not rethrow is observed here.
    /// </summary>
    internal void CompletePooledTaskWrapper(
        PooledPendingResponse pending,
        Task response,
        bool releaseSlot)
    {
        if (!response.IsCompleted &&
            _pending.Remove(pending.MessageId, pending, consumed: true))
        {
            pending.CompleteAbandonedAfterRemoval();
        }

        ObserveFault(response);
        if (releaseSlot)
        {
            ReleasePendingSlot();
        }

        pending.ReleaseWrapper();
    }
}
