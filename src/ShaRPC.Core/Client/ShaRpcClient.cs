using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

/// <summary>
/// ShaRPC client that sends requests and receives responses.
/// </summary>
public sealed class ShaRpcClient : IShaRpcClient
{
    private readonly ITransport _transport;
    private readonly ISerializer _serializer;
    private readonly ShaRpcPendingRequests _pendingRequests = new();
    private readonly TimeSpan _timeout;
    private int _messageIdCounter;
    private Task? _receiveTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ShaRpcClient(ITransport transport, ISerializer serializer, TimeSpan? timeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsConnected => _transport.IsConnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _transport.ConnectAsync(ct);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct);
    }

    public async Task InvokeAsync(
        string service,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct);
    }

    public async Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct);
    }

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        string? instanceId,
        CancellationToken ct)
    {
        var connection = EnsureConnected();
        var pending = ReservePendingRequest();
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
                connection,
                service,
                method,
                ct);
        }
        catch
        {
            _pendingRequests.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            throw;
        }
    }

    private Task<ReceivedResponse> SendRequestAsync(
        string service,
        string method,
        string? instanceId,
        CancellationToken ct)
    {
        var connection = EnsureConnected();
        var pending = ReservePendingRequest();
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
                connection,
                service,
                method,
                ct);
        }
        catch
        {
            _pendingRequests.Remove(pending.MessageId, pending.Completion.Task, consumed: true);
            throw;
        }
    }

    private (int MessageId, TaskCompletionSource<ReceivedResponse> Completion) ReservePendingRequest()
    {
        while (true)
        {
            var messageId = Interlocked.Increment(ref _messageIdCounter);
            if (messageId != 0 && _pendingRequests.TryAdd(messageId, out var tcs))
            {
                return (messageId, tcs);
            }
        }
    }

    private IConnection EnsureConnected()
    {
        var connection = _transport.Connection;
        if (connection == null || !_transport.IsConnected)
        {
            throw new ShaRpcConnectionException("Not connected to server.");
        }

        return connection;
    }

    private static RpcRequest CreateEnvelope(int messageId, string service, string method, string? instanceId) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
        };

    /// <summary>
    /// Registers the pending request, sends the already-framed request, and awaits the response.
    /// Ownership of <paramref name="frame"/> transfers here and it is disposed once sent. The response
    /// frame handed back through the pending TCS is only returned to the caller once the success check
    /// passes; on every other path it is disposed via <see cref="DisposeResultWhenAvailable"/>, so a
    /// response that races in after a timeout/cancel can never leak its rented buffer.
    /// </summary>
    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(
        int messageId,
        TaskCompletionSource<ReceivedResponse> tcs,
        Payload frame,
        IConnection connection,
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
                await connection.SendAsync(frame.Memory, ct);
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
                    received = await tcs.Task;
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    if (requestSent)
                    {
                        _ = SendCancelFrameAsync(connection, messageId);
                    }

                    // The linked token fires for both the caller's cancellation and the timeout;
                    // re-throw the former as-is and map the latter to a timeout.
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
            _pendingRequests.Remove(messageId, tcs.Task, consumed);
        }
    }

    private static async Task SendCancelFrameAsync(IConnection connection, int messageId)
    {
        try
        {
            using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
            await connection.SendAsync(frame.Memory, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation is best-effort; the request may already have completed or the connection closed.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _transport.IsConnected)
            {
                var connection = _transport.Connection;
                if (connection == null)
                {
                    break;
                }

                var frame = await connection.ReceiveAsync(ct);
                if (frame.Length == 0)
                {
                    frame.Dispose();
                    break;
                }

                var handedOff = false;
                try
                {
                    if (!MessageFramer.TryReadFrame(frame.Memory, out var messageId, out var messageType, out var envelope, out var payload))
                    {
                        continue;
                    }

                    if (messageType != MessageType.Response && messageType != MessageType.Error)
                    {
                        continue;
                    }

                    var response = _serializer.Deserialize<RpcResponse>(envelope);
                    handedOff = _pendingRequests.TryComplete(messageId, response, payload, frame);
                }
                finally
                {
                    if (!handedOff)
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            _pendingRequests.FailAll(new ShaRpcConnectionException("Connection lost.", ex));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // ignore
            }
        }

        _pendingRequests.CancelAll();

        _cts?.Dispose();
        await _transport.DisposeAsync();
    }
}
