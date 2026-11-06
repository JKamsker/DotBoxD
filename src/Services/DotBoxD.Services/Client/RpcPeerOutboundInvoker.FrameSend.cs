using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    private ValueTask SendOwnedFrameAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        if (_sendFrameAsync is { } sendFrameAsync)
        {
            try
            {
                return sendFrameAsync(frame, ct);
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        return SendOwnedFrameByMemoryAsync(frame, ct);
    }

    private async ValueTask SendOwnedFrameByMemoryAsync(PooledBufferWriter frame, CancellationToken ct)
    {
        try
        {
            await _sendAsync(frame.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            frame.Dispose();
        }
    }

    private Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        PendingReceivedResponse pending,
        PooledBufferWriter frame,
        string service,
        string method,
        RpcOutboundStreamSet outboundStreams,
        CancellationToken ct)
    {
        var deadline = GetRequestDeadline();
        Task? send;
        try
        {
            var sendTask = SendOwnedFrameAsync(frame, ct);
            if (sendTask.IsCompletedSuccessfully)
            {
                sendTask.GetAwaiter().GetResult();
                send = null;
            }
            else
            {
                send = sendTask.AsTask();
            }
        }
        catch (Exception ex)
        {
            // Let the awaiting method own the failure so cleanup stays in one place.
            send = Task.FromException(ex);
        }

        // The deadline was taken before the send started; arm it while the send may still be in flight.
        _pending.StartTimeout(pending, deadline);
        if (send is not null && ShouldRaceSend(send.IsCompleted, ct))
        {
            return AwaitSentFrameWithinDeadlineAsync(
                pending,
                send,
                deadline,
                service,
                method,
                outboundStreams,
                ct);
        }

        return AwaitSentFrameResponseAsync(
            pending,
            send ?? Task.CompletedTask,
            sendDetached: false,
            service,
            method,
            outboundStreams,
            ct);
    }

    // Kept parameter-lean: this state machine is allocated per call, including synchronous sends.
    // The send is awaited through ValueTask so the awaiter slot is shared with the stream cleanup
    // await below; a synchronously completed send arrives as Task.CompletedTask.
    private async Task<ReceivedResponse> AwaitSentFrameResponseAsync(
        PendingReceivedResponse pending,
        Task send,
        bool sendDetached,
        string service,
        string method,
        RpcOutboundStreamSet outboundStreams,
        CancellationToken ct)
    {
        var consumed = false;
        var requestSent = false;
        try
        {
            await new ValueTask(send).ConfigureAwait(false);

            // A detached send means the call ended before the request was written: its streams
            // never start, and the detached send owns the slot and the late cancel frame.
            requestSent = !sendDetached;
            if (requestSent)
            {
                outboundStreams.Start();
            }

            var callerCancellation = ct.CanBeCanceled
                ? ct.Register(static state => ((IPendingResponse)state!).CancelByCaller(), pending)
                : default;
            using (callerCancellation)
            {
                try
                {
                    var received = await pending.Task.ConfigureAwait(false);
                    if (!received.Response.IsSuccess)
                    {
                        ThrowRemoteError(received);
                    }

                    received.AttachOutboundStreams(outboundStreams);
                    consumed = true;
                    return received;
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Timeout)
                {
                    TrySendCancelForSentRequest(requestSent, pending.MessageId);
                    ct.ThrowIfCancellationRequested();
                    RpcTelemetry.RequestTimedOut();
                    throw new ServiceTimeoutException($"Request to {service}.{method} timed out.");
                }
                catch (OperationCanceledException) when (pending.CancellationKind == PendingCancellationKind.Caller)
                {
                    TrySendCancelForSentRequest(requestSent, pending.MessageId);
                    ct.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }
        finally
        {
            if (!consumed)
            {
                _pending.Remove(pending.MessageId, pending, consumed: false);
            }

            ReleasePendingSlotUnlessDetached(sendDetached);

            if (!consumed)
            {
                if (requestSent)
                {
                    // A running source's cancellation callback can join this invocation.
                    // Let its failure complete before waiting for source cleanup.
                    outboundStreams.DisposeInBackground();
                }
                else
                {
                    await outboundStreams.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private void TrySendCancelForSentRequest(bool requestSent, int messageId)
    {
        if (requestSent)
        {
            _cancelFrames.TrySend(messageId);
        }
    }

    private static void ThrowRemoteError(ReceivedResponse received)
    {
        var error = new RemoteServiceException(
            received.Response.ErrorMessage ?? "Unknown error",
            received.Response.ErrorType ?? "Unknown");
        received.Dispose();
        throw error;
    }
}
