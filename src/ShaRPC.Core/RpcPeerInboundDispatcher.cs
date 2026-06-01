using System.Collections.Concurrent;
using System.Threading.Channels;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;
internal sealed class RpcPeerInboundDispatcher
{
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeInbound = new();
    private readonly ConcurrentDictionary<int, Task> _activeTasks = new();
    private readonly InstanceRegistry _registry = new();
    private readonly ISerializer _serializer;
    private readonly RpcPeerResponseBuilder _responseBuilder;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly bool _dropIncomingWhenFull;
    private readonly Channel<RpcPeerInboundRequest>? _queue;
    private CancellationTokenSource? _queueCts;
    private Task? _worker;
    private int _stopped;

    public RpcPeerInboundDispatcher(
        ISerializer serializer,
        RpcPeerOptions options,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync)
    {
        _serializer = serializer;
        _responseBuilder = new RpcPeerResponseBuilder(
            serializer,
            _registry,
            _dispatchers,
            options.RejectInboundCalls);
        _sendAsync = sendAsync;
        if (!Enum.IsDefined(typeof(ShaRpcQueueFullMode), options.QueueFullMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.QueueFullMode,
                "Unknown queue full mode.");
        }
        if (options.InboundQueueCapacity is not { } capacity)
        {
            return;
        }
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                capacity,
                "Inbound queue capacity must be greater than zero.");
        }
        _dropIncomingWhenFull = options.QueueFullMode == ShaRpcQueueFullMode.DropIncoming;
        _queue = Channel.CreateBounded<RpcPeerInboundRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            // Wait lets DropIncoming detect a full queue via TryWrite and release state.
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public void Start(CancellationToken loopCt)
    {
        if (_queue is null)
        {
            return;
        }
        _queueCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        _worker = Task.Run(() => ProcessQueueAsync(_queueCts.Token));
    }

    public void AddDispatcher(IServiceDispatcher dispatcher)
    {
        if (!_dispatchers.TryAdd(dispatcher.ServiceName, dispatcher))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already provided.");
        }
    }

    public async ValueTask<bool> AcceptRequestAsync(
        Payload frame,
        int messageId,
        CancellationToken loopCt)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        if (!TryCreateInboundRequest(frame, messageId, loopCt, out var inbound, out var protocolError))
        {
            using var errorFrame = _responseBuilder.BuildProtocolErrorFrame(messageId, protocolError);
            await _sendAsync(errorFrame.Memory, loopCt).ConfigureAwait(false);
            return false;
        }

        if (_queue is null)
        {
            StartRequest(inbound);
            return true;
        }
        if (_dropIncomingWhenFull)
        {
            if (_queue.Writer.TryWrite(inbound))
            {
                return true;
            }
            ReleaseRequest(inbound);
            return false;
        }
        try
        {
            await _queue.Writer.WriteAsync(inbound, loopCt).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (loopCt.IsCancellationRequested)
        {
            ReleaseRequest(inbound);
            return false;
        }
        catch (ChannelClosedException)
        {
            ReleaseRequest(inbound);
            return false;
        }
    }

    public void Cancel(int messageId)
    {
        if (_activeInbound.TryGetValue(messageId, out var requestCts))
        {
            SafeCancel(requestCts);
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _queueCts?.Cancel();
        _queue?.Writer.TryComplete();

        foreach (var requestCts in _activeInbound.Values)
        {
            SafeCancel(requestCts);
        }

        if (_worker is not null)
        {
            await ObserveShutdownAsync(_worker).ConfigureAwait(false);
        }

        if (!_activeTasks.IsEmpty)
        {
            await ObserveShutdownAsync(Task.WhenAll(_activeTasks.Values)).ConfigureAwait(false);
        }

        DrainQueue();
        _registry.ReleaseAll();
        _queueCts?.Dispose();
    }

    private bool TryCreateInboundRequest(
        Payload frame,
        int messageId,
        CancellationToken loopCt,
        out RpcPeerInboundRequest inbound,
        out string protocolError)
    {
        inbound = default;
        if (!RpcPeerInboundRequestReader.TryRead(
            frame,
            _serializer,
            out var request,
            out var payload,
            out protocolError))
        {
            return false;
        }

        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        if (!_activeInbound.TryAdd(messageId, requestCts))
        {
            requestCts.Dispose();
            protocolError = "Duplicate request message id.";
            return false;
        }

        inbound = new RpcPeerInboundRequest(frame, request, messageId, payload, requestCts);
        return true;
    }

    private void StartRequest(RpcPeerInboundRequest inbound)
    {
        var task = ProcessRequestAsync(inbound);
        _activeTasks[inbound.MessageId] = task;
        if (task.IsCompleted)
        {
            _activeTasks.TryRemove(inbound.MessageId, out _);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue!.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var inbound))
                {
                    await ProcessRequestAsync(inbound).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
    }

    private async Task ProcessRequestAsync(RpcPeerInboundRequest inbound)
    {
        try
        {
            using (inbound.Frame)
            {
                using var responseFrame = await _responseBuilder.BuildAsync(
                    inbound.Request,
                    inbound.MessageId,
                    inbound.Body,
                    inbound.RequestCts.Token).ConfigureAwait(false);
                await _sendAsync(responseFrame.Memory, inbound.RequestCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (inbound.RequestCts.IsCancellationRequested)
        {
            // Cancelled work sends no response frame.
        }
        catch
        {
            // Dispatch/send failures are observed and swallowed per request.
        }
        finally
        {
            ReleaseRequest(inbound);
            _activeTasks.TryRemove(inbound.MessageId, out _);
        }
    }

    private void DrainQueue()
    {
        if (_queue is null)
        {
            return;
        }

        while (_queue.Reader.TryRead(out var inbound))
        {
            inbound.Frame.Dispose();
            ReleaseRequest(inbound);
        }
    }

    private void ReleaseRequest(RpcPeerInboundRequest inbound)
    {
        _activeInbound.TryRemove(inbound.MessageId, out _);
        inbound.RequestCts.Dispose();
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

    private static async Task ObserveShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Individual request tasks observe their own failures.
        }
    }
}
