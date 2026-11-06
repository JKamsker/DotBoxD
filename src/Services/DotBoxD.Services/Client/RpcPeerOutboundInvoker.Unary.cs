using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    private Task<TResponse> SendUnaryRequestAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        PendingUnaryResponse<TResponse> pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingUnaryRequest<TResponse>(service, method, ct);
        }
        catch (Exception ex)
        {
            return ToFaultedTask<TResponse>(ex);
        }

        PooledBufferWriter frame;
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId, streams: null);
            frame = MessageFramer.RentFrameRequest(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                request);
        }
        catch (Exception ex)
        {
            _pending.Remove(pending.MessageId, pending, consumed: false);
            ReleasePendingSlot();
            return ToFaultedOrCanceledTask<TResponse>(ct, ex);
        }

        return SendFrameAndReadUnaryResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            service,
            method,
            ct);
    }

    private Task<TResponse> SendUnaryRequestAsync<TResponse>(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        PendingUnaryResponse<TResponse> pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingUnaryRequest<TResponse>(service, method, ct);
        }
        catch (Exception ex)
        {
            return ToFaultedTask<TResponse>(ex);
        }

        PooledBufferWriter frame;
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId, streams: null);
            frame = MessageFramer.RentFrameMessage(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                ReadOnlySpan<byte>.Empty);
        }
        catch (Exception ex)
        {
            _pending.Remove(pending.MessageId, pending, consumed: false);
            ReleasePendingSlot();
            return ToFaultedTask<TResponse>(ex);
        }

        return SendFrameAndReadUnaryResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            service,
            method,
            ct);
    }

    private Task<TResponse> SendFrameAndReadUnaryResponseAsync<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        PooledBufferWriter frame,
        string service,
        string method,
        CancellationToken ct)
    {
        var deadline = GetRequestDeadline();
        ValueTask sendTask;
        try
        {
            sendTask = SendOwnedFrameAsync(frame, ct);
        }
        catch (Exception ex)
        {
            _pending.Remove(messageId, pending, consumed: false);
            ReleasePendingSlot();
            return ToFaultedTask<TResponse>(ex);
        }

        if (!ct.CanBeCanceled && sendTask.IsCompletedSuccessfully)
        {
            try
            {
                sendTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _pending.Remove(messageId, pending, consumed: false);
                ReleasePendingSlot();
                return ToFaultedTask<TResponse>(ex);
            }

            pending.EnableDirectCompletion(this);
            if (!pending.Task.IsCompleted)
            {
                _pending.StartTimeout(pending, deadline);
            }

            return pending.Task;
        }

        // The deadline was taken before the send started; arm it while the send may still be in flight.
        _pending.StartTimeout(pending, deadline);
        if (ShouldRaceSend(sendTask.IsCompleted, ct))
        {
            return AwaitUnarySendWithinDeadlineAsync(
                messageId,
                pending,
                sendTask.AsTask(),
                deadline,
                service,
                method,
                ct);
        }

        return AwaitUnaryResponseAsync(
            messageId,
            pending,
            sendTask,
            sendDetached: false,
            service,
            method,
            ct);
    }

    private async Task<TResponse> AwaitUnaryResponseAsync<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        ValueTask sendTask,
        bool sendDetached,
        string service,
        string method,
        CancellationToken ct)
    {
        var responseOwned = false;
        var requestSent = false;
        try
        {
            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // A channel can observe caller cancellation and then report its own send
                // failure. The caller's cancellation remains the terminal outcome while the
                // send is in progress, and no cancel frame is sent because delivery is unknown.
                _pending.TryCancel(messageId, pending, PendingCancellationKind.Caller);
                ct.ThrowIfCancellationRequested();
                throw;
            }

            // A detached send means the call ended (for example timed out) before the request was
            // written; the detached send owns the slot and the late cancel frame.
            requestSent = !sendDetached;

            // A synchronous send can complete the response and cancel the caller before this
            // continuation gets a chance to register the token callback. Cancellation still
            // wins while the send is in progress, even when the response was delivered inline.
            if (ct.IsCancellationRequested && TryEndUnaryForCanceledCaller(messageId, pending, requestSent))
            {
                ct.ThrowIfCancellationRequested();
            }

            var callerCancellation = ct.CanBeCanceled
                ? ct.Register(static state => ((IPendingResponse)state!).CancelByCaller(), pending)
                : default;
            using (callerCancellation)
            {
                try
                {
                    var response = await pending.Task.ConfigureAwait(false);
                    responseOwned = true;
                    return response;
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Timeout)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    RpcTelemetry.RequestTimedOut();
                    throw new ServiceTimeoutException($"Request to {service}.{method} timed out.");
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Caller)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw;
                }
            }

        }
        finally
        {
            if (!responseOwned)
            {
                _pending.Remove(messageId, pending, consumed: false);
            }

            ReleasePendingSlotUnlessDetached(sendDetached);
        }
    }

    // Ends a call whose caller cancelled before the response wait began, sending the one cancel frame
    // a written request owes when it ends by caller cancellation or by a timeout that won the race.
    // A caller cancellation that won while the request was being written already sent its frame. A
    // pending another outcome claimed but has not completed yet is left to the response wait, whose
    // handlers send the frame for a timeout; returns false then.
    private bool TryEndUnaryForCanceledCaller<TResponse>(
        int messageId,
        PendingUnaryResponse<TResponse> pending,
        bool requestSent)
    {
        if (_pending.TryCancel(messageId, pending, PendingCancellationKind.Caller))
        {
            _cancelFrames.TrySend(messageId);
            return true;
        }

        if (!pending.Task.IsCompleted)
        {
            return false;
        }

        if (requestSent && pending.CancellationKind == PendingCancellationKind.Timeout)
        {
            _cancelFrames.TrySend(messageId);
        }

        return true;
    }

    private static Task<T> ToFaultedTask<T>(Exception error) =>
        Task.FromException<T>(error);

    private static Task<T> ToFaultedOrCanceledTask<T>(CancellationToken ct, Exception error) =>
        ct.IsCancellationRequested ? Task.FromCanceled<T>(ct) : ToFaultedTask<T>(error);
}
