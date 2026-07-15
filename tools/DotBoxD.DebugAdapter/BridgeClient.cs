using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.DebugAdapter.Diagnostics;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class BridgeClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    private readonly NamedPipeClientStream _pipe;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maxMessageBytes;
    private readonly int _maxFrameBytes;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<PluginDebugEnvelope> _events = Channel.CreateUnbounded<PluginDebugEnvelope>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly BridgeSourceChangeDispatcher _sourceChanges;
    private readonly Task _eventLoop;
    private readonly Task _readLoop;
    private readonly Task _sourceChangeLoop;
    private int _requestId;

    private BridgeClient(
        NamedPipeClientStream pipe,
        string sessionToken,
        int maxMessageBytes,
        TimeSpan requestTimeout)
    {
        _pipe = pipe;
        _requestTimeout = requestTimeout;
        _maxMessageBytes = maxMessageBytes;
        _maxFrameBytes = BridgeProtocolIO.WrappedEnvelopeLimit(maxMessageBytes);
        SessionToken = sessionToken;
        _sourceChanges = new BridgeSourceChangeDispatcher(AcknowledgeSourceChangeAsync);
        _eventLoop = DispatchEventsAsync(_lifetime.Token);
        _sourceChangeLoop = _sourceChanges.RunAsync(_lifetime.Token);
        _readLoop = ReadLoopAsync(_lifetime.Token);
    }

    public string SessionToken { get; }
    public Func<PluginDebugEnvelope, ValueTask>? EventReceiver { get; set; }
    public Func<long, ValueTask> SourcesChangedReceiver
    {
        set => _sourceChanges.SetReceiver(value);
    }
    public static async Task<BridgeClient> ConnectAsync(
        string pipeName,
        string discoveryToken,
        CancellationToken cancellationToken,
        TimeSpan? requestTimeout = null)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await BridgeProtocolIO.WriteAsync(
                    pipe,
                    new { kind = "authenticate", token = discoveryToken },
                    BridgeProtocolIO.WrappedEnvelopeLimit(BridgeProtocolIO.DefaultEnvelopeLimit),
                    cancellationToken)
                .ConfigureAwait(false);
            using var response = await BridgeProtocolIO.ReadAsync(
                    pipe,
                    BridgeProtocolIO.WrappedEnvelopeLimit(BridgeProtocolIO.DefaultEnvelopeLimit),
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new EndOfStreamException("Bridge closed during authentication.");
            var root = response.RootElement;
            if (!root.GetProperty("success").GetBoolean())
            {
                throw new UnauthorizedAccessException("Plugin debug bridge authentication failed.");
            }

            return new BridgeClient(
                pipe,
                root.GetProperty("sessionToken").GetString()!,
                root.GetProperty("maxFrameBytes").GetInt32(),
                requestTimeout ?? DefaultRequestTimeout);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }
    public static async Task<BridgeClient> ConnectByProcessIdAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => await BridgeClientDiscovery.ConnectByProcessIdAsync(processId, timeout, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<JsonElement> SendAsync(
        string kind,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        var requestToken = timeout.Token;
        var id = Interlocked.Increment(ref _requestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var request = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["kind"] = kind
        };
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                request[argument.Key] = argument.Value;
            }
        }

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Duplicate bridge request ID.");
        }

        try
        {
            AdapterDiagnostics.Write($"bridge send {kind} {id}");
            await WriteAsync(request, requestToken).ConfigureAwait(false);
            var response = await completion.Task.WaitAsync(requestToken).ConfigureAwait(false);
            AdapterDiagnostics.Write($"bridge response {kind} {id}");
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DebugAdapterException("bridgeTimeout", "The plugin debug bridge did not respond in time.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask<JsonElement> RemoteAsync(
        string command,
        object? payload,
        CancellationToken cancellationToken)
    {
        AdapterDiagnostics.Write("bridge remote " + command);
        var envelope = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            command,
            Guid.NewGuid().ToString("N"),
            SessionToken,
            JsonSerializer.SerializeToElement(payload ?? new { }));
        var encoded = PluginDebugProtocol.Encode(envelope, _maxMessageBytes);
        var bridge = await SendAsync(
                "exchange",
                new Dictionary<string, object?> { ["payload"] = Convert.ToBase64String(encoded) },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(bridge);
        var remote = PluginDebugProtocol.Decode(
            Convert.FromBase64String(bridge.GetProperty("payload").GetString()!),
            _maxMessageBytes);
        var response = remote.Payload;
        if (!response.GetProperty("success").GetBoolean())
        {
            var error = response.GetProperty("error");
            throw new DebugAdapterException(
                error.GetProperty("code").GetString()!,
                error.GetProperty("message").GetString()!);
        }

        return response.GetProperty("body").Clone();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _pipe.Dispose();
        try
        {
            await Task.WhenAll(_readLoop, _eventLoop, _sourceChangeLoop).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Expected connection shutdown.
        }

        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var frame = await BridgeProtocolIO.ReadAsync(_pipe, _maxFrameBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (frame is null)
                {
                    return;
                }

                var root = frame.RootElement;
                if (root.TryGetProperty("id", out var id))
                {
                    if (_pending.TryGetValue(id.GetString()!, out var completion))
                    {
                        completion.TrySetResult(root.Clone());
                    }

                    continue;
                }

                var kind = root.GetProperty("kind").GetString();
                if (kind == "event")
                {
                    var envelope = PluginDebugProtocol.Decode(
                        Convert.FromBase64String(root.GetProperty("payload").GetString()!),
                        _maxMessageBytes);
                    await _events.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
                }
                else if (kind == "sourcesChanged")
                {
                    await _sourceChanges.EnqueueAsync(root.GetProperty("version").GetInt64(), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _events.Writer.TryComplete();
            _sourceChanges.Complete();
            var exception = new EndOfStreamException("Plugin debug bridge disconnected.");
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private async Task DispatchEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var envelope in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (EventReceiver is { } handler)
            {
                await handler(envelope).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask AcknowledgeSourceChangeAsync(long version, CancellationToken cancellationToken)
    {
        _ = await SendAsync(
                "sourcesChangedDone",
                new Dictionary<string, object?> { ["version"] = version },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask WriteAsync(object message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await BridgeProtocolIO.WriteAsync(_pipe, message, _maxFrameBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void EnsureSuccess(JsonElement response)
    {
        if (!response.GetProperty("success").GetBoolean())
        {
            throw new DebugAdapterException("bridgeError", response.GetProperty("error").GetString()!);
        }
    }
}
