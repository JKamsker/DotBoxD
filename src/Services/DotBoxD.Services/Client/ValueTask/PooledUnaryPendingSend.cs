using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;

namespace DotBoxD.Services.Client;

internal static class PooledUnaryPendingSend
{
    // With a finite RequestTimeout the deadline is armed before waiting on a backpressured send, and a
    // cancelable caller token is raced as well. The source is consumed once, through a Task observer,
    // so the wait can end on whichever of the send and the response (including its timeout or caller
    // cancellation) finishes first. Allocation stays on this slow path.
    public static async ValueTask<TResponse> AwaitWithinDeadlineAsync<TResponse>(
        RpcPeerOutboundInvoker owner,
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
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
            ownsSlot = await owner.AwaitSendBeforeCompletionAsync(
                    sendTask,
                    response,
                    pending,
                    deadline,
                    callerToken)
                .ConfigureAwait(false);

            // A detached send owns the slot and the late cancel frame; the call has already ended.
            if (ownsSlot)
            {
                callerCancellation = pending.RegisterCallerCancellation(callerToken);
            }

            try
            {
                return await response.ConfigureAwait(false);
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

    // Used for a send that already completed, or one with neither a finite RequestTimeout nor a
    // cancelable caller token to race; the deadline variant above owns every other send in flight.
    public static async ValueTask<TResponse> AwaitFrameAsync<TResponse>(
        RpcPeerOutboundInvoker owner,
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
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
                return await pending.ValueTask.ConfigureAwait(false);
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

    public static async ValueTask<TResponse> AwaitMemoryAsync<TResponse>(
        RpcPeerOutboundInvoker owner,
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
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
                return await pending.ValueTask.ConfigureAwait(false);
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
