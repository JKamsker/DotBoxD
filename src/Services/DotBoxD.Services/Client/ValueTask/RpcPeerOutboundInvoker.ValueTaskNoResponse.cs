using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public ValueTask InvokeValueAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        SendNoResponseValueRequestAsync(service, method, request, instanceId: null, ct);

    public ValueTask InvokeValueAsync(
        string service,
        string method,
        CancellationToken ct = default) =>
        SendNoResponseValueRequestAsync(service, method, instanceId: null, ct);

    public ValueTask InvokeValueOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdValueTask();
        }

        return SendNoResponseValueRequestAsync(service, method, request, instanceId, ct);
    }

    public ValueTask InvokeValueOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdValueTask();
        }

        return SendNoResponseValueRequestAsync(service, method, instanceId, ct);
    }

    private ValueTask SendNoResponseValueRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        if (!_enableLowAllocationValueTaskInvocations)
        {
            var task = instanceId is null
                ? InvokeAsync(service, method, request, ct)
                : InvokeOnInstanceAsync(service, instanceId, method, request, ct);
            return new ValueTask(task);
        }

        PendingValueTaskNoResponse pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingValueTaskNoResponseRequest(service, method, ct);
        }
        catch (Exception ex)
        {
            return new ValueTask(Task.FromException(ex));
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
            CompletePooledSetupFailure(pending);
            return new ValueTask(Task.FromException(ex));
        }

        return SendFrameAndReadNoResponseValueAsync(pending.MessageId, pending, frame, ct);
    }

    private ValueTask SendNoResponseValueRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        if (!_enableLowAllocationValueTaskInvocations)
        {
            var task = instanceId is null
                ? InvokeAsync(service, method, ct)
                : InvokeOnInstanceAsync(service, instanceId, method, ct);
            return new ValueTask(task);
        }

        PendingValueTaskNoResponse pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingValueTaskNoResponseRequest(service, method, ct);
        }
        catch (Exception ex)
        {
            return new ValueTask(Task.FromException(ex));
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
            CompletePooledSetupFailure(pending);
            return new ValueTask(Task.FromException(ex));
        }

        return SendFrameAndReadNoResponseValueAsync(pending.MessageId, pending, frame, ct);
    }

    private ValueTask SendFrameAndReadNoResponseValueAsync(
        int messageId,
        PendingValueTaskNoResponse pending,
        PooledBufferWriter frame,
        CancellationToken ct)
    {
        var sendFrameAsync = _sendFrameAsync;
        if (sendFrameAsync is not null)
        {
            ValueTask sendValueTask;
            try
            {
                sendValueTask = sendFrameAsync(frame, ct);
            }
            catch (Exception ex)
            {
                frame.Dispose();
                CompletePooledSetupFailure(pending);
                return new ValueTask(Task.FromException(ex));
            }

            if (sendValueTask.IsCompletedSuccessfully)
            {
                try
                {
                    sendValueTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    CompletePooledSetupFailure(pending);
                    return new ValueTask(Task.FromException(ex));
                }

                return ct.CanBeCanceled
                    ? CompleteCallerCancelablePooledNoResponseSendDirectly(pending, ct)
                    : CompletePooledNoResponseSendDirectly(pending);
            }

            pending.TransferSetupToWrapper();
            return PooledNoResponsePendingSend.AwaitFrameAsync(
                this,
                messageId,
                pending,
                sendValueTask,
                ct);
        }

        Task sendTask;
        try
        {
            sendTask = _sendAsync(frame.WrittenMemory, ct);
        }
        catch (Exception ex)
        {
            frame.Dispose();
            CompletePooledSetupFailure(pending);
            return new ValueTask(Task.FromException(ex));
        }

        if (sendTask.IsCompletedSuccessfully)
        {
            frame.Dispose();
            return ct.CanBeCanceled
                ? CompleteCallerCancelablePooledNoResponseSendDirectly(pending, ct)
                : CompletePooledNoResponseSendDirectly(pending);
        }

        pending.TransferSetupToWrapper();
        return PooledNoResponsePendingSend.AwaitMemoryAsync(
            this,
            messageId,
            pending,
            frame,
            sendTask,
            ct);
    }

    private ValueTask CompleteCallerCancelablePooledNoResponseSendDirectly(
        PendingValueTaskNoResponse pending,
        CancellationToken callerToken)
    {
        try
        {
            pending.RegisterOwnedCallerCancellation(callerToken);
            StartPooledTimeoutIfNeeded(pending);
        }
        catch (Exception ex)
        {
            CompletePooledSetupFailure(pending);
            return new ValueTask(Task.FromException(ex));
        }

        return pending.GetDirectValueTask(this);
    }

    private ValueTask CompletePooledNoResponseSendDirectly(PendingValueTaskNoResponse pending)
    {
        StartPooledTimeoutIfNeeded(pending);
        return pending.GetDirectValueTask(this);
    }
}
