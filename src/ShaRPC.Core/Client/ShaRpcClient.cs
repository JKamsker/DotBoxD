using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RpcResponse>> _pendingRequests = new();
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
        using var requestPayload = _serializer.SerializeToPayload(request);
        var response = await SendRequestAsync(service, method, requestPayload.Memory, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(response.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        var response = await SendRequestAsync(service, method, ReadOnlyMemory<byte>.Empty, instanceId: null, ct);
        return _serializer.Deserialize<TResponse>(response.Payload);
    }

    public async Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var requestPayload = _serializer.SerializeToPayload(request);
        await SendRequestAsync(service, method, requestPayload.Memory, instanceId: null, ct);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var requestPayload = _serializer.SerializeToPayload(request);
        var response = await SendRequestAsync(service, method, requestPayload.Memory, instanceId, ct);
        return _serializer.Deserialize<TResponse>(response.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
    {
        var response = await SendRequestAsync(service, method, ReadOnlyMemory<byte>.Empty, instanceId, ct);
        return _serializer.Deserialize<TResponse>(response.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        using var requestPayload = _serializer.SerializeToPayload(request);
        await SendRequestAsync(service, method, requestPayload.Memory, instanceId, ct);
    }

    private async Task<RpcResponse> SendRequestAsync(
        string service,
        string method,
        ReadOnlyMemory<byte> payload,
        string? instanceId,
        CancellationToken ct)
    {
        if (_transport.Connection == null || !_transport.IsConnected)
        {
            throw new ShaRpcConnectionException("Not connected to server.");
        }

        var messageId = Interlocked.Increment(ref _messageIdCounter);
        var request = new RpcRequest
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            Payload = payload,
            InstanceId = instanceId,
        };

        var tcs = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests.TryAdd(messageId, tcs);

        try
        {
            using (var frame = MessageFramer.FrameMessage(_serializer, messageId, MessageType.Request, request))
            {
                await _transport.Connection.SendAsync(frame.Memory, ct);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(_timeout, timeoutCts.Token));

                if (completedTask != tcs.Task)
                {
                    throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
                }

                var response = await tcs.Task;

                if (!response.IsSuccess)
                {
                    throw new ShaRpcRemoteException(
                        response.ErrorMessage ?? "Unknown error",
                        response.ErrorType ?? "Unknown");
                }

                return response;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ShaRpcTimeoutException($"Request to {service}.{method} timed out.");
            }
        }
        finally
        {
            _pendingRequests.TryRemove(messageId, out _);
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

                using var frame = await connection.ReceiveAsync(ct);
                if (frame.Length == 0)
                {
                    break;
                }

                if (!MessageFramer.TryReadFrame(frame.Memory, out var messageId, out var messageType, out var envelope))
                {
                    continue;
                }

                if (messageType == MessageType.Response || messageType == MessageType.Error)
                {
                    // Safety invariant: under MessagePackSecurity.UntrustedData the serializer copies
                    // the nested RpcResponse.Payload into a fresh heap array rather than aliasing
                    // `envelope`. That makes `response.Payload` outlive `frame`, so the rented frame
                    // can be disposed at the end of this loop iteration even though the awaiting caller
                    // deserializes response.Payload later on another thread. Do NOT switch the inner
                    // payload to a zero-copy slice of `frame` without extending the frame's lifetime.
                    var response = _serializer.Deserialize<RpcResponse>(envelope);
                    if (_pendingRequests.TryGetValue(messageId, out var tcs))
                    {
                        tcs.TrySetResult(response);
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
            // complete all pending requests with error
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new ShaRpcConnectionException("Connection lost.", ex));
            }
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

        // Complete all pending requests
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }

        _cts?.Dispose();
        await _transport.DisposeAsync();
    }
}
