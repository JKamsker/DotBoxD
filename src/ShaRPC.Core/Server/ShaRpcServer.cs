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
    private readonly ShaRpcServerResponseBuilder _responseBuilder;
    private readonly ConcurrentDictionary<IConnection, Task> _connections = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public ShaRpcServer(IServerTransport transport, ISerializer serializer)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _responseBuilder = new ShaRpcServerResponseBuilder(_serializer, _dispatchers);
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
            catch (OperationCanceledException) { }
        }

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
                var registry = new InstanceRegistry();
                var connectionTask = HandleConnectionAsync(connection, registry, ct);
                _connections.TryAdd(connection, connectionTask);

                _ = connectionTask.ContinueWith(_ => _connections.TryRemove(connection, out _), TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) { }
        }
    }

    private async Task HandleConnectionAsync(IConnection connection, IInstanceRegistry registry, CancellationToken ct)
    {
        var activeRequests = new ConcurrentDictionary<int, CancellationTokenSource>();
        var activeTasks = new ConcurrentDictionary<int, Task>();

        try
        {
            while (connection.IsConnected && !ct.IsCancellationRequested)
            {
                var data = await connection.ReceiveAsync(ct);
                if (data.Length == 0)
                {
                    data.Dispose();
                    break;
                }

                ProcessMessage(connection, registry, data, activeRequests, activeTasks, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            foreach (var request in activeRequests.Values)
            {
                SafeCancel(request);
            }

            await WaitForActiveRequestsAsync(activeTasks.Values);

            registry.ReleaseAll();
            await connection.DisposeAsync();
        }
    }

    private void ProcessMessage(
        IConnection connection,
        IInstanceRegistry registry,
        Payload data,
        ConcurrentDictionary<int, CancellationTokenSource> activeRequests,
        ConcurrentDictionary<int, Task> activeTasks,
        CancellationToken ct)
    {
        if (!MessageFramer.TryReadFrameHeader(data.Memory, out var messageId, out var messageType))
        {
            data.Dispose();
            return;
        }

        if (messageType == MessageType.Cancel)
        {
            if (activeRequests.TryGetValue(messageId, out var requestCts))
            {
                SafeCancel(requestCts);
            }

            data.Dispose();
            return;
        }

        if (messageType != MessageType.Request ||
            !MessageFramer.TryReadFrame(data.Memory, out _, out _, out var envelope, out var payload))
        {
            data.Dispose();
            return;
        }

        RpcRequest request;
        try
        {
            request = _serializer.Deserialize<RpcRequest>(envelope);
        }
        catch
        {
            data.Dispose();
            throw;
        }

        var dispatchCts = new CancellationTokenSource();
        if (!activeRequests.TryAdd(messageId, dispatchCts))
        {
            dispatchCts.Dispose();
            data.Dispose();
            return;
        }

        var task = ProcessRequestAsync(
            connection,
            registry,
            data,
            request,
            messageId,
            payload,
            activeRequests,
            activeTasks,
            dispatchCts);
        activeTasks[messageId] = task;
        if (task.IsCompleted)
        {
            activeTasks.TryRemove(messageId, out _);
        }
    }

    private async Task ProcessRequestAsync(
        IConnection connection,
        IInstanceRegistry registry,
        Payload data,
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        ConcurrentDictionary<int, CancellationTokenSource> activeRequests,
        ConcurrentDictionary<int, Task> activeTasks,
        CancellationTokenSource requestCts)
    {
        try
        {
            using (data)
            {
                using var responseFrame = await _responseBuilder.BuildAsync(
                    request,
                    messageId,
                    payload,
                    registry,
                    requestCts.Token);
                await connection.SendAsync(responseFrame.Memory, requestCts.Token);
            }
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            // Remote cancellation or server shutdown; no response frame is sent for cancelled work.
        }
        catch (Exception)
        {
            // Log error
        }
        finally
        {
            activeRequests.TryRemove(messageId, out _);
            activeTasks.TryRemove(messageId, out _);
            requestCts.Dispose();
        }
    }

    private static async Task WaitForActiveRequestsAsync(IEnumerable<Task> activeRequests)
    {
        try
        {
            await Task.WhenAll(activeRequests).ConfigureAwait(false);
        }
        catch
        {
            // Individual request tasks observe and swallow dispatch/send failures.
        }
    }

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
