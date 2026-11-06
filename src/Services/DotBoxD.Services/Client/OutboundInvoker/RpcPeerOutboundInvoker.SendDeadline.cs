using DotBoxD.Services.Buffers;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Client;

// A finite RequestTimeout bounds the whole call, send included. The deadline is taken before the
// request send starts. A synchronous send arms it afterwards exactly as before; a send that goes
// asynchronous arms it immediately and races the send against the pending response, so a send that
// never completes fails with the same ServiceTimeoutException as a late response. Only the deadline
// ends the wait for a send early; a response or connection failure that lands first still waits for
// the send's own outcome, up to the deadline.
//
// A send still in flight when the call ends is not cancelled: cancelling a write that already started
// closes the whole connection, which would turn one slow call into a failure of every multiplexed
// call. The detached send keeps owning (and finally disposing) its frame, keeps the call's admission
// slot until it finishes so MaxPendingRequests still bounds the frames queued behind a stuck send,
// has its failure observed, and is followed by a cancel frame when a timed-out request is written late.
internal sealed partial class RpcPeerOutboundInvoker
{
    private long GetRequestDeadline() =>
        _hasFiniteTimeout ? PendingRequestTimeoutScheduler.GetDeadline(_timeout) : long.MaxValue;

    /// <summary>
    /// Waits for an in-flight request send while the call's deadline is armed. Returns
    /// <see langword="true"/> once the send finished (rethrowing its failure), or <see langword="false"/>
    /// when the send was detached (see <see cref="WaitForSendWithinDeadlineAsync"/>); a detached send
    /// owns the call's admission slot.
    /// </summary>
    internal async ValueTask<bool> AwaitSendBeforeCompletionAsync(
        Task send,
        Task completion,
        IPendingResponse pending,
        long deadline)
    {
        await WaitForSendWithinDeadlineAsync(send, completion, pending, deadline).ConfigureAwait(false);
        if (TryDetachUnfinishedSend(send, pending))
        {
            return false;
        }

        await send.ConfigureAwait(false);
        return true;
    }

    // The Task-backed paths keep their existing state machines unchanged for synchronous sends; only a
    // send that went asynchronous under a finite timeout pays for this race and a second state machine.
    private async Task<TResponse> AwaitUnarySendWithinDeadlineAsync<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        Task send,
        long deadline,
        string service,
        string method,
        CancellationToken ct)
    {
        await WaitForSendWithinDeadlineAsync(send, pending.Task, pending, deadline).ConfigureAwait(false);
        var detached = TryDetachUnfinishedSend(send, pending);
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
        await WaitForSendWithinDeadlineAsync(send, pending.Task, pending, deadline).ConfigureAwait(false);
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

    // Returns once the send finished, the call timed out, or the deadline passed. A call that ended
    // another way while its send is in flight (a response, or a connection failure) keeps waiting for
    // the send as it always has, so the send's own outcome stays authoritative, but never past the
    // deadline: a send that is still stuck then is detached rather than holding the call open.
    private static async Task WaitForSendWithinDeadlineAsync(
        Task send,
        Task completion,
        IPendingResponse pending,
        long deadline)
    {
        _ = await Task.WhenAny(send, completion).ConfigureAwait(false);
        if (send.IsCompleted || pending.CancellationKind == PendingCancellationKind.Timeout)
        {
            return;
        }

        var remaining = PendingRequestTimeoutScheduler.GetRemainingMilliseconds(deadline);
        if (remaining == 0)
        {
            return;
        }

        using var delayCancellation = new CancellationTokenSource();
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

        DetachSend(
            send,
            pending.MessageId,
            cancelWhenWritten: pending.CancellationKind == PendingCancellationKind.Timeout);
        return true;
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
                // The request reached the remote after its caller timed out; tell it to stop.
                _owner._cancelFrames.TrySend(_messageId);
            }
        }
    }
}
