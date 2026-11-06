using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;

namespace DotBoxD.Services.Client;

internal static class PooledNoResponsePendingSend
{
    // With a finite RequestTimeout the deadline is armed before waiting on a backpressured send. The
    // source is consumed once, through a Task observer, so the wait can end on whichever of the send
    // and the response (including its timeout) finishes first. Allocation stays on this slow path.
    public static async ValueTask AwaitWithinDeadlineAsync(
        RpcPeerOutboundInvoker owner,
        int messageId,
        PendingValueTaskNoResponse pending,
        Task sendTask,
        long deadline,
        CancellationToken callerToken)
    {
        owner.StartPooledTimeoutIfNeeded(pending, deadline);
        var response = pending.ValueTask.AsTask();
        var callerCancellation = default(CancellationTokenRegistration);
        var ownsSlot = true;
        try
        {
            ownsSlot = await owner.AwaitSendBeforeCompletionAsync(sendTask, response, pending, deadline)
                .ConfigureAwait(false);

            // A detached send owns the slot and the late cancel frame; the call has already ended.
            if (ownsSlot)
            {
                callerCancellation = pending.RegisterCallerCancellation(callerToken);
            }

            try
            {
                await response.ConfigureAwait(false);
            }
            catch (ServiceTimeoutException) when (
                pending.CancellationKind == PendingCancellationKind.Timeout)
            {
                TrySendCancelForWrittenRequest(owner, ownsSlot, messageId);
                callerToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException) when (
                pending.CancellationKind == PendingCancellationKind.Caller)
            {
                TrySendCancelForWrittenRequest(owner, ownsSlot, messageId);
                callerToken.ThrowIfCancellationRequested();
                throw;
            }
        }
        finally
        {
            try
            {
                callerCancellation.Dispose();
            }
            finally
            {
                owner.CompletePooledTaskWrapper(pending, response, releaseSlot: ownsSlot);
            }
        }
    }

    // Used without a finite RequestTimeout, or for a send that already failed synchronously; the
    // deadline variant above owns every send still in flight under a finite timeout.
    public static async ValueTask AwaitFrameAsync(
        RpcPeerOutboundInvoker owner,
        int messageId,
        PendingValueTaskNoResponse pending,
        ValueTask sendTask,
        CancellationToken callerToken)
    {
        var callerCancellation = default(CancellationTokenRegistration);
        var pendingConsumed = false;
        try
        {
            await sendTask.ConfigureAwait(false);
            callerCancellation = pending.RegisterCallerCancellation(callerToken);
            try
            {
                await pending.ValueTask.ConfigureAwait(false);
            }
            catch (ServiceTimeoutException) when (
                pending.CancellationKind == PendingCancellationKind.Timeout)
            {
                owner.TrySendPooledCancel(messageId);
                callerToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException) when (
                pending.CancellationKind == PendingCancellationKind.Caller)
            {
                owner.TrySendPooledCancel(messageId);
                callerToken.ThrowIfCancellationRequested();
                throw;
            }
            finally
            {
                pendingConsumed = true;
            }
        }
        finally
        {
            Complete(owner, pending, pendingConsumed, callerCancellation);
        }
    }

    public static async ValueTask AwaitMemoryAsync(
        RpcPeerOutboundInvoker owner,
        int messageId,
        PendingValueTaskNoResponse pending,
        PooledBufferWriter frame,
        Task sendTask,
        CancellationToken callerToken)
    {
        var callerCancellation = default(CancellationTokenRegistration);
        var pendingConsumed = false;
        try
        {
            using (frame)
            {
                await sendTask.ConfigureAwait(false);
            }

            callerCancellation = pending.RegisterCallerCancellation(callerToken);
            try
            {
                await pending.ValueTask.ConfigureAwait(false);
            }
            catch (ServiceTimeoutException) when (
                pending.CancellationKind == PendingCancellationKind.Timeout)
            {
                owner.TrySendPooledCancel(messageId);
                callerToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException) when (
                pending.CancellationKind == PendingCancellationKind.Caller)
            {
                owner.TrySendPooledCancel(messageId);
                callerToken.ThrowIfCancellationRequested();
                throw;
            }
            finally
            {
                pendingConsumed = true;
            }
        }
        finally
        {
            Complete(owner, pending, pendingConsumed, callerCancellation);
        }
    }

    private static void TrySendCancelForWrittenRequest(
        RpcPeerOutboundInvoker owner,
        bool requestWritten,
        int messageId)
    {
        if (requestWritten)
        {
            owner.TrySendPooledCancel(messageId);
        }
    }

    private static void Complete(
        RpcPeerOutboundInvoker owner,
        PooledPendingResponse pending,
        bool pendingConsumed,
        CancellationTokenRegistration callerCancellation)
    {
        try
        {
            callerCancellation.Dispose();
        }
        finally
        {
            owner.CompletePooledWrapper(pending, pendingConsumed);
        }
    }
}
