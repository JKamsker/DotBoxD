using DotBoxD.Services.Buffers;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Client;

// A finite RequestTimeout bounds the whole call, send included. The deadline is taken before the
// request send starts. A synchronous send arms it afterwards exactly as before; a send that goes
// asynchronous arms it immediately and races the send against the pending response, so a send that
// never completes fails with the same ServiceTimeoutException as a late response.
//
// Frames are written atomically, so the caller's token no longer interrupts a write that already
// started. A send that goes asynchronous with a cancelable caller token (finite or infinite timeout)
// therefore also races caller cancellation: the call ends with the caller's OperationCanceledException
// instead of waiting for the write. Only the deadline or caller cancellation ends the wait for a send
// early; a response or connection failure that lands first still waits for the send's own outcome,
// up to the deadline or caller cancellation.
//
// A send still in flight when the call ends is not cancelled: frames are atomic, so a write that already
// started cannot be interrupted without corrupting the shared connection for every multiplexed call.
// The detached send keeps owning (and finally disposing) its frame, keeps the call's admission
// slot until it finishes so MaxPendingRequests still bounds the frames queued behind a stuck send,
// has its failure observed, and is followed by a cancel frame when a timed-out or caller-cancelled
// request is written late.
internal sealed partial class RpcPeerOutboundInvoker
{
    private long GetRequestDeadline() =>
        _hasFiniteTimeout ? PendingRequestTimeoutScheduler.GetDeadline(_timeout) : long.MaxValue;

    // Only a send that went asynchronous races its call's deadline and caller cancellation; a
    // synchronous send, or one with neither to race, keeps the allocation-free plain wrappers.
    private bool ShouldRaceSend(bool sendCompleted, CancellationToken ct) =>
        !sendCompleted && (_hasFiniteTimeout || ct.CanBeCanceled);

    /// <summary>
    /// Waits for an in-flight request send while the call's deadline and caller cancellation are armed.
    /// Returns <see langword="true"/> once the send finished (rethrowing its failure), or
    /// <see langword="false"/> when the send was detached (see <see cref="WaitForSendWithinDeadlineAsync"/>);
    /// a detached send owns the call's admission slot.
    /// </summary>
    internal async ValueTask<bool> AwaitSendBeforeCompletionAsync(
        Task send,
        Task completion,
        IPendingResponse pending,
        long deadline,
        CancellationToken ct)
    {
        await WaitForSendWithinDeadlineAsync(send, completion, pending, deadline, ct).ConfigureAwait(false);
        if (TryDetachUnfinishedSend(send, pending))
        {
            return false;
        }

        await send.ConfigureAwait(false);
        return true;
    }

    // The Task-backed paths keep their existing state machines unchanged for synchronous sends; only a
    // send that went asynchronous under a finite timeout or a cancelable caller token pays for this
    // race and a second state machine.
    private async Task<TResponse> AwaitUnarySendWithinDeadlineAsync<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        Task send,
        long deadline,
        string service,
        string method,
        CancellationToken ct)
    {
        await WaitForSendWithinDeadlineAsync(send, pending.Task, pending, deadline, ct).ConfigureAwait(false);
        var detached = TryDetachUnfinishedSend(send, pending);
        if (!detached)
        {
            SendCancelIfCanceledWhileWriting(send, pending);
        }

        return await AwaitUnaryResponseAsync(
            messageId,
            pending,
            detached ? default : new ValueTask(send),
            detached,
            service,
            method,
            ct).ConfigureAwait(false);
    }

    private async Task<ReceivedResponse> AwaitSentFrameWithinDeadlineAsync(
        PendingReceivedResponse pending,
        Task send,
        long deadline,
        string service,
        string method,
        RpcOutboundStreamSet outboundStreams,
        CancellationToken ct)
    {
        await WaitForSendWithinDeadlineAsync(send, pending.Task, pending, deadline, ct).ConfigureAwait(false);
        var detached = TryDetachUnfinishedSend(send, pending);
        return await AwaitSentFrameResponseAsync(
            pending,
            detached ? Task.CompletedTask : send,
            detached,
            service,
            method,
            outboundStreams,
            ct).ConfigureAwait(false);
    }

    // Returns once the send finished, or the call timed out or was cancelled by its caller. Caller
    // cancellation cancels the pending response, so it races a response, timeout or connection failure
    // on the pending's single removal. A call that ended another way while its send is in flight (a
    // response, or a connection failure) keeps waiting for the send as it always has, so the send's own
    // outcome stays authoritative, but never past the deadline or caller cancellation: a send that is
    // still stuck then is detached rather than holding the call open.
    private static async Task WaitForSendWithinDeadlineAsync(
        Task send,
        Task completion,
        IPendingResponse pending,
        long deadline,
        CancellationToken ct)
    {
        var callerCancellation = ct.CanBeCanceled
            ? ct.Register(static state => ((IPendingResponse)state!).CancelByCaller(), pending)
            : default;
        using (callerCancellation)
        {
            _ = await Task.WhenAny(send, completion).ConfigureAwait(false);
        }

        if (send.IsCompleted || pending.CancellationKind != PendingCancellationKind.None)
        {
            return;
        }

        await WaitForSendOutcomeAsync(send, deadline, ct).ConfigureAwait(false);
    }

    private static async Task WaitForSendOutcomeAsync(Task send, long deadline, CancellationToken ct)
    {
        var remaining = deadline == long.MaxValue
            ? Timeout.Infinite
            : PendingRequestTimeoutScheduler.GetRemainingMilliseconds(deadline);
        if (remaining == 0 || ct.IsCancellationRequested)
        {
            return;
        }

        using var delayCancellation = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        _ = await Task.WhenAny(send, Task.Delay(remaining, delayCancellation.Token)).ConfigureAwait(false);
        delayCancellation.Cancel();
    }

    private void ReleasePendingSlotUnlessDetached(bool sendDetached)
    {
        // A detached send releases the slot itself once it finishes.
        if (!sendDetached)
        {
            ReleasePendingSlot();
        }
    }

    private bool TryDetachUnfinishedSend(Task send, IPendingResponse pending)
    {
        if (send.IsCompleted)
        {
            return false;
        }

        // A call that timed out or was cancelled by its caller still owes the remote a cancel frame if
        // its request is written late; a response or connection failure already ended it remotely.
        DetachSend(
            send,
            pending.MessageId,
            cancelWhenWritten: pending.CancellationKind != PendingCancellationKind.None);
        return true;
    }

    // Caller cancellation that won the race while the request was still being written already removed
    // the pending, so the unary wrapper finds nothing left to cancel; the written request still owes the
    // remote a cancel frame. The other wrappers send it from their caller-cancellation handler.
    private void SendCancelIfCanceledWhileWriting(Task send, IPendingResponse pending)
    {
        if (send.IsCompletedSuccessfully && pending.CancellationKind == PendingCancellationKind.Caller)
        {
            _cancelFrames.TrySend(pending.MessageId);
        }
    }

    private static async Task DisposeFrameAfterSendAsync(PooledBufferWriter frame, Task send)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }

    private void DetachSend(Task send, int messageId, bool cancelWhenWritten)
    {
        var detached = new DetachedRequestSend(this, send, messageId, cancelWhenWritten);

        // A stuck send can outlive the caller; UnsafeOnCompleted keeps the caller's ambient context
        // from being captured and kept alive with it.
        send.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(detached.Complete);
    }

    private static void ObserveFault(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() => _ = task.Exception);
    }

    private sealed class DetachedRequestSend
    {
        private readonly RpcPeerOutboundInvoker _owner;
        private readonly Task _send;
        private readonly int _messageId;
        private readonly bool _cancelWhenWritten;

        public DetachedRequestSend(
            RpcPeerOutboundInvoker owner,
            Task send,
            int messageId,
            bool cancelWhenWritten)
        {
            _owner = owner;
            _send = send;
            _messageId = messageId;
            _cancelWhenWritten = cancelWhenWritten;
        }

        public void Complete()
        {
            // The caller already has its outcome; a late send failure only needs observing.
            _ = _send.Exception;

            // Free the slot before the cancel frame so the next admitted call is never refused
            // merely because this cleanup has not finished.
            _owner.ReleasePendingSlot();
            if (_cancelWhenWritten && _send.IsCompletedSuccessfully)
            {
                // The request reached the remote after its caller timed out or cancelled; tell it to stop.
                _owner._cancelFrames.TrySend(_messageId);
            }
        }
    }
}
