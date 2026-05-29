using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Server;

/// <summary>
/// ShaRPC server that accepts connections and dispatches requests to registered services.
/// </summary>
public sealed class ShaRpcServer : IShaRpcServer
{
    private readonly IServerTransport _transport;
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ConcurrentDictionary<IConnection, Task> _connections = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public ShaRpcServer(IServerTransport transport, ISerializer serializer)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Registers a service dispatcher.
    /// </summary>
    public void RegisterDispatcher(IServiceDispatcher dispatcher)
    {
        if (!_dispatchers.TryAdd(dispatcher.ServiceName, dispatcher))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already registered.");
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_cts != null)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _transport.StartAsync(ct);
        _acceptTask = AcceptConnectionsAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Wait for all connections to close
        var connectionTasks = _connections.Values.ToArray();
        if (connectionTasks.Length > 0)
        {
            await Task.WhenAll(connectionTasks);
        }

        await _transport.StopAsync(ct);
        _cts.Dispose();
        _cts = null;
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _transport.AcceptAsync(ct);
                // Each connection gets its own instance registry — sub-service identifiers
                // are scoped to the connection that created them.
                var registry = new InstanceRegistry();
                var connectionTask = HandleConnectionAsync(connection, registry, ct);
                _connections.TryAdd(connection, connectionTask);

                // Clean up completed connections
                _ = connectionTask.ContinueWith(_ => _connections.TryRemove(connection, out _), TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Log and continue accepting
            }
        }
    }

    private async Task HandleConnectionAsync(IConnection connection, IInstanceRegistry registry, CancellationToken ct)
    {
        try
        {
            while (connection.IsConnected && !ct.IsCancellationRequested)
            {
                using var data = await connection.ReceiveAsync(ct);
                if (data.Length == 0)
                {
                    break; // Connection closed
                }

                await ProcessMessageAsync(connection, registry, data, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception)
        {
            // Log error
        }
        finally
        {
            // Drop every sub-service instance tied to this connection so user code's
            // server-side state cannot outlive the connection that produced it.
            registry.ReleaseAll();
            await connection.DisposeAsync();
        }
    }

    private async Task ProcessMessageAsync(IConnection connection, IInstanceRegistry registry, Payload data, CancellationToken ct)
    {
        if (!MessageFramer.TryReadFrame(data.Memory, out var messageId, out var messageType, out var envelope, out var payload))
        {
            return;
        }

        if (messageType != MessageType.Request)
        {
            return; // Server only handles requests
        }

        // Safety invariant: `payload` is a zero-copy slice of the frame buffer (`data`), which the
        // caller keeps alive for the duration of this method. Dispatchers deserialize their
        // arguments synchronously from `payload` up front and never retain the memory, so the slice
        // never outlives `data`. The result is serialized into a separate frame buffer, not `data`.
        var request = _serializer.Deserialize<RpcRequest>(envelope);

        using var responseFrame = await BuildResponseFrameAsync(request, messageId, payload, registry, ct);
        await connection.SendAsync(responseFrame.Memory, ct);
    }

    /// <summary>
    /// Builds the response frame for a request. On the success path the response envelope is written
    /// first and the dispatcher serializes the method result straight into the trailing payload, so
    /// there is no separate result buffer to allocate and copy. If the dispatcher throws (unknown
    /// method, instance not found, or a user exception) the half-built frame is discarded and a fresh
    /// error frame is returned instead. The caller owns the returned <see cref="Payload"/>.
    /// </summary>
    private async ValueTask<Payload> BuildResponseFrameAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        CancellationToken ct)
    {
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
            // Route by instance: a non-null InstanceId means the call targets a server-side
            // sub-service that the client got a handle to earlier.
            await (request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, writer, ct)
                : dispatcher.DispatchOnInstanceAsync(request.InstanceId, request.MethodName, payload, _serializer, registry, writer, ct));
        }
        catch (Exception ex)
        {
            // The success envelope already written to `writer` is abandoned; the using disposes it.
            return BuildErrorFrame(messageId, ex.Message, ex.GetType().Name);
        }

        return MessageFramer.FinishFrame(writer, envelopeLength);
    }

    private Payload BuildErrorFrame(int messageId, string errorMessage, string errorType) =>
        MessageFramer.FrameMessage(
            _serializer,
            messageId,
            MessageType.Error,
            new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorType = errorType,
            },
            ReadOnlySpan<byte>.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        await _transport.DisposeAsync();
    }
}
