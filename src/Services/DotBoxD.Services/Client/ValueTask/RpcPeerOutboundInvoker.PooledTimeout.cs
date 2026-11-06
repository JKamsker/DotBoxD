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

    // A send still in flight under a finite RequestTimeout races its deadline; any other unfinished
    // send (no finite timeout, or one that already failed synchronously) keeps the plain wrapper.
    private ValueTask<TResponse> AwaitPooledUnaryFrameSendAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        ValueTask send,
        long deadline,
        CancellationToken ct) =>
        _hasFiniteTimeout && !send.IsCompleted
            ? PooledUnaryPendingSend.AwaitWithinDeadlineAsync(this, messageId, pending, send.AsTask(), deadline, ct)
            : PooledUnaryPendingSend.AwaitFrameAsync(this, messageId, pending, send, ct);

    private ValueTask<TResponse> AwaitPooledUnaryMemorySendAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
        PooledBufferWriter frame,
        Task send,
        long deadline,
        CancellationToken ct) =>
        _hasFiniteTimeout && !send.IsCompleted
            ? PooledUnaryPendingSend.AwaitWithinDeadlineAsync(
                this,
                messageId,
                pending,
                DisposeFrameAfterSendAsync(frame, send),
                deadline,
                ct)
            : PooledUnaryPendingSend.AwaitMemoryAsync(this, messageId, pending, frame, send, ct);

    private ValueTask AwaitPooledNoResponseFrameSendAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
        ValueTask send,
        long deadline,
        CancellationToken ct) =>
        _hasFiniteTimeout && !send.IsCompleted
            ? PooledNoResponsePendingSend.AwaitWithinDeadlineAsync(this, messageId, pending, send.AsTask(), deadline, ct)
            : PooledNoResponsePendingSend.AwaitFrameAsync(this, messageId, pending, send, ct);

    private ValueTask AwaitPooledNoResponseMemorySendAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
        PooledBufferWriter frame,
        Task send,
        long deadline,
        CancellationToken ct) =>
        _hasFiniteTimeout && !send.IsCompleted
            ? PooledNoResponsePendingSend.AwaitWithinDeadlineAsync(
                this,
                messageId,
                pending,
                DisposeFrameAfterSendAsync(frame, send),
                deadline,
                ct)
            : PooledNoResponsePendingSend.AwaitMemoryAsync(this, messageId, pending, frame, send, ct);

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
