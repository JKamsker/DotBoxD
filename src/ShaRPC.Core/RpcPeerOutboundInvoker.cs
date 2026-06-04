using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core;

internal sealed class RpcPeerOutboundInvoker : IRpcInvoker
{
    private readonly ISerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly int _maxPendingRequests;
    private readonly Action _ensureStarted;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly ShaRpcPendingRequests _pending = new();
    private readonly RpcPeerCancelFrameSender _cancelFrames;
    private int _messageIdCounter;
    private int _pendingCount;

    public RpcPeerOutboundInvoker(
        ISerializer serializer,
        RpcPeerOptions options,
        Action ensureStarted,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync)
    {
        _serializer = serializer;
        _timeout = options.RequestTimeout;
        _maxPendingRequests = options.MaxPendingRequests;
        _ensureStarted = ensureStarted;
        _sendAsync = sendAsync;
        _cancelFrames = new RpcPeerCancelFrameSender(sendAsync);
    }

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, request, instanceId: null, ct).ConfigureAwait(false);
    }

    public async Task InvokeAsync(string service, string method, CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, request, instanceId, ct).ConfigureAwait(false);
    }

    public async Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
    }

    public bool TryCompleteResponse(int messageId, Payload frame)
    {
        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out var messageType, out var envelope, out var payload))
        {
            _pending.TryFail(
                messageId,
                new ShaRpcProtocolException("Malformed response frame."));
            return false;
        }

        RpcResponse response;
        try
        {
            response = _serializer.Deserialize<RpcResponse>(envelope);
        }
        catch
        {
            _pending.TryFail(
                messageId,
                new ShaRpcProtocolException("Malformed response envelope."));
            return false;
        }

        if (messageType == MessageType.Error && response.IsSuccess)
        {
            _pending.TryFail(
                messageId,
                new ShaRpcProtocolException("Malformed error response frame."));
            return false;
        }

        return _pending.TryComplete(messageId, response, payload, frame);
    }

    public void FailPending(Exception error) => _pending.FailAll(error);

    public Task StopCancelFramesAsync() => _cancelFrames.StopAsync();

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        ValidateTarget(service, method);
        _ensureStarted();
        var pending = ReservePendingRequest(ct);
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId);
            var frame = MessageFramer.FrameRequest(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                request);
            return SendFrameAndAwaitAsync(
                pending.MessageId,
                pending.Completion,
                frame,
                service,
                method,
                ct);
        }
        catch
        {
            // Frame construction (serialization) threw before SendFrameAndAwaitAsync took
            // ownership of the reserved slot, so release it here; otherwise the admission gate
            // leaks one slot per serialization failure and eventually rejects every call.
            _pending.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            ReleasePendingSlot();
            throw;
        }
    }

    private Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        ValidateTarget(service, method);
        _ensureStarted();
        var pending = ReservePendingRequest(ct);
        try
        {
            var envelope = CreateEnvelope(pending.MessageId, service, method, instanceId);
            var frame = MessageFramer.FrameMessage(
                _serializer,
                pending.MessageId,
                MessageType.Request,
                envelope,
                ReadOnlySpan<byte>.Empty);
            return SendFrameAndAwaitAsync(
                pending.MessageId,
                pending.Completion,
                frame,
                service,
                method,
                ct);
        }
        catch
        {
            // Frame construction (serialization) threw before SendFrameAndAwaitAsync took
            // ownership of the reserved slot, so release it here; otherwise the admission gate
            // leaks one slot per serialization failure and eventually rejects every call.
            _pending.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            ReleasePendingSlot();
            throw;
        }
    }

    private (int MessageId, TaskCompletionSource<ReceivedResponse> Completion) ReservePendingRequest(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _pendingCount) > _maxPendingRequests)
        {
            Interlocked.Decrement(ref _pendingCount);
            throw new ShaRpcException("Maximum pending requests reached.");
        }

        try
        {
            for (var attempts = 0; attempts < _maxPendingRequests; attempts++)
            {
                ct.ThrowIfCancellationRequested();
                var messageId = Interlocked.Increment(ref _messageIdCounter);
                if (messageId != 0 && _pending.TryAdd(messageId, out var tcs))
                {
                    return (messageId, tcs);
                }
            }

            throw new ShaRpcException("Unable to reserve a request message id.");
        }
        catch
        {
            Interlocked.Decrement(ref _pendingCount);
            throw;
        }
    }

    private void ReleasePendingSlot() => Interlocked.Decrement(ref _pendingCount);

    private static void ValidateTarget(string service, string method)
    {
        if (string.IsNullOrEmpty(service))
        {
            throw new ArgumentException("Service name must not be null or empty.", nameof(service));
        }

        if (string.IsNullOrEmpty(method))
        {
            throw new ArgumentException("Method name must not be null or empty.", nameof(method));
        }
    }

    private static RpcRequest CreateEnvelope(
        int messageId,
        string service,
        string method,
        string? instanceId) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
        };

    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        TaskCompletionSource<ReceivedResponse> tcs,
        Payload frame,
        string service,
        string method,
        CancellationToken ct)
    {
        var consumed = false;
        var requestSent = false;
        try
        {
            using (frame)
            {
                await _sendAsync(frame.Memory, ct).ConfigureAwait(false);
                requestSent = true;
            }

            using var timeoutCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            timeoutCts.CancelAfter(_timeout);

            ReceivedResponse received;
            // Cancel through the pending-request table rather than the TCS directly, so the timeout and
            // an incoming response race on a single atomic removal: whichever removes the entry first
            // wins and the loser is a guaranteed no-op. Cancelling the TCS directly could win the race
            // against TryComplete and discard an already-delivered response as a spurious timeout.
            using (timeoutCts.Token.Register(
                static state =>
                {
                    var pendingState = ((ShaRpcPendingRequests Pending, int MessageId))state!;
                    pendingState.Pending.TryCancel(pendingState.MessageId);
                },
                (_pending, messageId)))
            {
                try
                {
                    received = await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    if (requestSent)
                    {
                        _cancelFrames.TrySend(messageId);
                    }

                    ct.ThrowIfCancellationRequested();
                    throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
                }
            }

            if (!received.Response.IsSuccess)
            {
                throw new ShaRpcRemoteException(
                    received.Response.ErrorMessage ?? "Unknown error",
                    received.Response.ErrorType ?? "Unknown");
            }

            consumed = true;
            return received;
        }
        finally
        {
            _pending.Remove(messageId, tcs.Task, consumed);
            ReleasePendingSlot();
        }
    }
}
