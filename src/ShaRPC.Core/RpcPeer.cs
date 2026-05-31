using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// One symmetric side of a ShaRPC connection. A peer can <see cref="Provide{TService}(TService)"/>
/// implementations the other side calls, and <see cref="Get{TService}"/> proxies to call the
/// implementations the other side provides — either, or both. A single read loop demuxes inbound
/// frames: <see cref="MessageType.Response"/>/<see cref="MessageType.Error"/> complete this peer's
/// pending calls, while <see cref="MessageType.Request"/>/<see cref="MessageType.Cancel"/> are
/// dispatched to the implementations this peer provides.
/// </summary>
public sealed class RpcPeer : IAsyncDisposable, IRpcInvoker
{
    private readonly IRpcChannel _channel;
    private readonly ISerializer _serializer;
    private readonly TimeSpan _timeout;
    private readonly bool _rejectInboundCalls;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ShaRpcPendingRequests _pending = new();
    private readonly InstanceRegistry _registry = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeInbound = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _messageIdCounter;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;
    private int _started;
    private int _disposed;

    private RpcPeer(IRpcChannel channel, ISerializer serializer, RpcPeerOptions options)
    {
        _channel = channel;
        _serializer = serializer;
        _timeout = options.RequestTimeout;
        _rejectInboundCalls = options.RejectInboundCalls;
    }

    /// <summary>Creates a peer over <paramref name="channel"/>. Call <see cref="Start"/> to begin
    /// the read loop (invoking a method also starts it implicitly).</summary>
    public static RpcPeer Over(IRpcChannel channel, ISerializer serializer, RpcPeerOptions? options = null)
    {
        if (channel is null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        return new RpcPeer(channel, serializer, options ?? new RpcPeerOptions());
    }

    /// <summary>Gets whether the underlying channel is still connected.</summary>
    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _channel.IsConnected;

    /// <summary>The remote endpoint string of the underlying channel.</summary>
    public string RemoteEndpoint => _channel.RemoteEndpoint;

    /// <summary>Raised when the read loop ends (gracefully or with an error).</summary>
    public event EventHandler<RpcDisconnectedEventArgs>? Disconnected;

    /// <summary>Raised when the read loop fails with a non-cancellation exception.</summary>
    public event Action<Exception>? ReadError;

    /// <summary>Provides a local implementation of <typeparamref name="TService"/> for the other
    /// side to call.</summary>
    public RpcPeer Provide<TService>(TService implementation)
        where TService : class
    {
        if (implementation is null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        return Provide(ShaRpcServiceRegistry.CreateDispatcher<TService>(implementation));
    }

    /// <summary>Provides a service via an explicit dispatcher.</summary>
    public RpcPeer Provide(IServiceDispatcher dispatcher)
    {
        if (dispatcher is null)
        {
            throw new ArgumentNullException(nameof(dispatcher));
        }

        if (!_dispatchers.TryAdd(dispatcher.ServiceName, dispatcher))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already provided.");
        }

        return this;
    }

    /// <summary>Creates a proxy to call <typeparamref name="TService"/> on the other side.</summary>
    public TService Get<TService>()
        where TService : class =>
        ShaRpcServiceRegistry.CreateProxy<TService>(this);

    /// <summary>Begins the read loop. Idempotent; safe to call from a fluent chain.</summary>
    public RpcPeer Start()
    {
        EnsureStarted();
        return this;
    }

    private void EnsureStarted()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(RpcPeer));
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    // ---------------- outbound calls (IRpcInvoker) ----------------

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId: null, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, request, instanceId: null, ct).ConfigureAwait(false);
    }

    public async Task InvokeAsync(string service, string method, CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, instanceId: null, ct).ConfigureAwait(false);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, request, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default)
    {
        using var received = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    public async Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, request, instanceId, ct).ConfigureAwait(false);
    }

    public async Task InvokeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default)
    {
        using var _ = await SendRequestAsync(service, method, instanceId, ct).ConfigureAwait(false);
    }

    private Task<ReceivedResponse> SendRequestAsync<TRequest>(string service, string method, TRequest request, string? instanceId, CancellationToken ct)
    {
        EnsureStarted();
        var messageId = Interlocked.Increment(ref _messageIdCounter);
        var envelope = CreateEnvelope(messageId, service, method, instanceId);
        var frame = MessageFramer.FrameRequest(_serializer, messageId, MessageType.Request, envelope, request);
        return SendFrameAndAwaitAsync(messageId, frame, service, method, ct);
    }

    private Task<ReceivedResponse> SendRequestAsync(string service, string method, string? instanceId, CancellationToken ct)
    {
        EnsureStarted();
        var messageId = Interlocked.Increment(ref _messageIdCounter);
        var envelope = CreateEnvelope(messageId, service, method, instanceId);
        var frame = MessageFramer.FrameMessage(_serializer, messageId, MessageType.Request, envelope, ReadOnlySpan<byte>.Empty);
        return SendFrameAndAwaitAsync(messageId, frame, service, method, ct);
    }

    private static RpcRequest CreateEnvelope(int messageId, string service, string method, string? instanceId) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
        };

    private async Task<ReceivedResponse> SendFrameAndAwaitAsync(int messageId, Payload frame, string service, string method, CancellationToken ct)
    {
        TaskCompletionSource<ReceivedResponse>? tcs = null;
        var consumed = false;
        var requestSent = false;
        try
        {
            tcs = _pending.Add(messageId);
            using (frame)
            {
                await SendRawAsync(frame.Memory, ct).ConfigureAwait(false);
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
            using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
            await SendRawAsync(frame.Memory, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation is best-effort.
        }
    }

    private async Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _channel.SendAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ---------------- read loop + inbound dispatch ----------------

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        Exception? readError = null;
        try
        {
            while (!ct.IsCancellationRequested && _channel.IsConnected)
            {
                Payload frame;
                try
                {
                    frame = await _channel.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                if (frame.Length == 0)
                {
                    frame.Dispose();
                    break;
                }

                var disposeFrame = true;
                try
                {
                    if (!MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out var messageType))
                    {
                        continue;
                    }

                    switch (messageType)
                    {
                        case MessageType.Response:
                        case MessageType.Error:
                            disposeFrame = !TryCompleteResponse(messageId, frame);
                            break;
                        case MessageType.Request:
                            HandleInboundRequest(frame, messageId, ct);
                            disposeFrame = false;
                            break;
                        case MessageType.Cancel:
                            if (_activeInbound.TryGetValue(messageId, out var requestCts))
                            {
                                SafeCancel(requestCts);
                            }

                            break;
                    }
                }
                finally
                {
                    if (disposeFrame)
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            readError = ex;
            _pending.FailAll(new ShaRpcConnectionException("Connection lost.", ex));
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                if (readError is not null)
                {
                    ReadError?.Invoke(readError);
                }

                Disconnected?.Invoke(this, new RpcDisconnectedEventArgs(_channel.RemoteEndpoint, readError));
            }
        }
    }

    private bool TryCompleteResponse(int messageId, Payload frame)
    {
        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out _, out var envelope, out var payload))
        {
            return false;
        }

        var response = _serializer.Deserialize<RpcResponse>(envelope);
        return _pending.TryComplete(messageId, response, payload, frame);
    }

    private void HandleInboundRequest(Payload frame, int messageId, CancellationToken loopCt)
    {
        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out _, out var envelope, out var payload))
        {
            frame.Dispose();
            return;
        }

        RpcRequest request;
        try
        {
            request = _serializer.Deserialize<RpcRequest>(envelope);
        }
        catch
        {
            frame.Dispose();
            return;
        }

        var dispatchCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        if (!_activeInbound.TryAdd(messageId, dispatchCts))
        {
            dispatchCts.Dispose();
            frame.Dispose();
            return;
        }

        var task = ProcessRequestAsync(frame, request, messageId, payload, dispatchCts);
        _activeTasks[messageId] = task;
        if (task.IsCompleted)
        {
            _activeTasks.TryRemove(messageId, out _);
        }
    }

    private async Task ProcessRequestAsync(Payload frame, RpcRequest request, int messageId, ReadOnlyMemory<byte> payload, CancellationTokenSource requestCts)
    {
        try
        {
            using (frame)
            {
                using var responseFrame = await BuildResponseFrameAsync(request, messageId, payload, requestCts.Token).ConfigureAwait(false);
                await SendRawAsync(responseFrame.Memory, requestCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            // Cancelled work sends no response frame.
        }
        catch
        {
            // Dispatch/send failures are observed and swallowed per-request.
        }
        finally
        {
            _activeInbound.TryRemove(messageId, out _);
            _activeTasks.TryRemove(messageId, out _);
            requestCts.Dispose();
        }
    }

    private async ValueTask<Payload> BuildResponseFrameAsync(RpcRequest request, int messageId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (_rejectInboundCalls)
        {
            return BuildErrorFrame(messageId, "This peer does not accept inbound calls.", "ShaRpcInboundRejected");
        }

        if (!_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
        {
            return BuildErrorFrame(messageId, $"Service '{request.ServiceName}' not found.", nameof(ShaRpcNotFoundException));
        }

        using var writer = new PooledBufferWriter(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize);
        MessageFramer.WriteFramePrefix(writer, messageId, MessageType.Response);
        var envelopeStart = writer.WrittenCount;
        _serializer.Serialize(writer, new RpcResponse { MessageId = messageId, IsSuccess = true });
        var envelopeLength = writer.WrittenCount - envelopeStart;

        try
        {
            await (request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, _registry, writer, ct)
                : dispatcher.DispatchOnInstanceAsync(request.InstanceId, request.MethodName, payload, _serializer, _registry, writer, ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildErrorFrame(messageId, ex.Message, ex.GetType().Name);
        }

        return MessageFramer.FinishFrame(writer, envelopeLength);
    }

    private Payload BuildErrorFrame(int messageId, string errorMessage, string errorType) =>
        MessageFramer.FrameMessage(
            _serializer,
            messageId,
            MessageType.Error,
            new RpcResponse { MessageId = messageId, IsSuccess = false, ErrorMessage = errorMessage, ErrorType = errorType },
            ReadOnlySpan<byte>.Empty);

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed while the connection was closing.
        }
    }

    /// <summary>Closes the peer and its channel. Idempotent.</summary>
    public Task CloseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return DisposeAsync().AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts?.Cancel();

        foreach (var requestCts in _activeInbound.Values)
        {
            SafeCancel(requestCts);
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        try
        {
            await Task.WhenAll(_activeTasks.Values).ConfigureAwait(false);
        }
        catch
        {
            // Individual request tasks observe their own failures.
        }

        _pending.CancelAll();
        _registry.ReleaseAll();

        _cts?.Dispose();
        _sendLock.Dispose();
        await _channel.DisposeAsync().ConfigureAwait(false);
    }
}
