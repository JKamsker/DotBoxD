using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;

namespace DotBoxD.Services.Client;

internal static class PooledNoResponsePendingSend
{
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
            owner.StartPooledTimeoutIfNeeded(pending);
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
            owner.StartPooledTimeoutIfNeeded(pending);
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
