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
    private readonly Action _ensureStarted;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly ShaRpcPendingRequests _pending = new();
    private int _messageIdCounter;

    public RpcPeerOutboundInvoker(
        ISerializer serializer,
        TimeSpan timeout,
        Action ensureStarted,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync)
    {
        _serializer = serializer;
        _timeout = timeout;
        _ensureStarted = ensureStarted;
        _sendAsync = sendAsync;
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
        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out _, out var envelope, out var payload))
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

        return _pending.TryComplete(messageId, response, payload, frame);
    }

    public void FailPending(Exception error) => _pending.FailAll(error);

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        _ensureStarted();
        var messageId = GetNextMessageId();
        var envelope = CreateEnvelope(messageId, service, method, instanceId);
        var frame = MessageFramer.FrameRequest(_serializer, messageId, MessageType.Request, envelope, request);
        return SendFrameAndAwaitAsync(messageId, frame, service, method, ct);
    }

    private Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        _ensureStarted();
        var messageId = GetNextMessageId();
        var envelope = CreateEnvelope(messageId, service, method, instanceId);
        var frame = MessageFramer.FrameMessage(
            _serializer,
            messageId,
            MessageType.Request,
            envelope,
            ReadOnlySpan<byte>.Empty);
        return SendFrameAndAwaitAsync(messageId, frame, service, method, ct);
    }

    private int GetNextMessageId()
    {
        int messageId;
        do
        {
            messageId = Interlocked.Increment(ref _messageIdCounter);
        }
        while (messageId == 0 || _pending.Contains(messageId));

        return messageId;
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
        Payload frame,
        string service,
        string method,
        CancellationToken ct)
    {
        TaskCompletionSource<ReceivedResponse>? tcs = null;
        var consumed = false;
        var requestSent = false;
        try
        {
            tcs = _pending.Add(messageId);
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
            using (timeoutCts.Token.Register(
                static state => ((TaskCompletionSource<ReceivedResponse>)state!).TrySetCanceled(),
                tcs))
            {
                try
                {
                    received = await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    if (requestSent)
                    {
                        _ = SendCancelFrameAsync(messageId);
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
            if (tcs is null)
            {
                frame.Dispose();
            }
            else
            {
                _pending.Remove(messageId, tcs.Task, consumed);
            }
        }
    }

    private async Task SendCancelFrameAsync(int messageId)
    {
        try
        {
            using var frame = MessageFramer.FrameToPayload(
                messageId,
                MessageType.Cancel,
                ReadOnlySpan<byte>.Empty);
            await _sendAsync(frame.Memory, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation is best-effort.
        }
    }
}
