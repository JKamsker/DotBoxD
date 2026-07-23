using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public ValueTask<TResponse> InvokeValueAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        SendUnaryValueRequestAsync<TRequest, TResponse>(service, method, request, instanceId: null, ct);

    public ValueTask<TResponse> InvokeValueAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) =>
        SendUnaryValueRequestAsync<TResponse>(service, method, instanceId: null, ct);

    public ValueTask<TResponse> InvokeValueOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdValueTask<TResponse>();
        }

        return SendUnaryValueRequestAsync<TRequest, TResponse>(service, method, request, instanceId, ct);
    }

    public ValueTask<TResponse> InvokeValueOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        if (instanceId is null)
        {
            return MissingInstanceIdValueTask<TResponse>();
        }

        return SendUnaryValueRequestAsync<TResponse>(service, method, instanceId, ct);
    }

    private ValueTask<TResponse> SendUnaryValueRequestAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        if (!_enableLowAllocationValueTaskInvocations)
        {
            return new ValueTask<TResponse>(
                SendUnaryRequestAsync<TRequest, TResponse>(service, method, request, instanceId, ct));
        }

        PendingValueTaskUnaryResponse<TResponse> pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingValueTaskUnaryRequest<TResponse>(service, method, ct);
        }
        catch (Exception ex)
        {
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
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
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        return SendFrameAndReadUnaryValueResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            ct);
    }

    private ValueTask<TResponse> SendUnaryValueRequestAsync<TResponse>(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        if (!_enableLowAllocationValueTaskInvocations)
        {
            return new ValueTask<TResponse>(
                SendUnaryRequestAsync<TResponse>(service, method, instanceId, ct));
        }

        PendingValueTaskUnaryResponse<TResponse> pending;
        try
        {
            ValidateTargetAndStart(service, method, ct);
            pending = ReservePendingValueTaskUnaryRequest<TResponse>(service, method, ct);
        }
        catch (Exception ex)
        {
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
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
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        return SendFrameAndReadUnaryValueResponseAsync<TResponse>(
            pending.MessageId,
            pending,
            frame,
            ct);
    }

    private ValueTask<TResponse> SendFrameAndReadUnaryValueResponseAsync<TResponse>(
        int messageId,
        PendingValueTaskUnaryResponse<TResponse> pending,
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
                return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
            }

            if (sendValueTask.IsCompletedSuccessfully && !ct.CanBeCanceled)
            {
                try
                {
                    sendValueTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    CompletePooledSetupFailure(pending);
                    return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
                }

                StartPooledTimeoutIfNeeded(pending);
                return pending.GetDirectValueTask(this);
            }

            pending.TransferSetupToWrapper();
            return PooledUnaryPendingSend.AwaitFrameAsync(this, messageId, pending, sendValueTask, ct);
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
            return new ValueTask<TResponse>(ToFaultedTask<TResponse>(ex));
        }

        if (sendTask.IsCompletedSuccessfully && !ct.CanBeCanceled)
        {
            frame.Dispose();
            if (_hasFiniteTimeout && !pending.CompletionStarted)
            {
                _pending.StartTimeout(pending, _timeout);
            }

            return pending.GetDirectValueTask(this);
        }

        pending.TransferSetupToWrapper();
        return PooledUnaryPendingSend.AwaitMemoryAsync(this, messageId, pending, frame, sendTask, ct);
    }
}
