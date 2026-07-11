using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.DebugAdapter;

internal sealed class BridgeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readLoop;
    private int _requestId;

    private BridgeClient(NamedPipeClientStream pipe, string sessionToken)
    {
        _pipe = pipe;
        SessionToken = sessionToken;
        _readLoop = ReadLoopAsync(_lifetime.Token);
    }

    public string SessionToken { get; }

    public Func<PluginDebugEnvelope, ValueTask>? EventReceiver { get; set; }

    public Func<ValueTask>? SourcesChangedReceiver { get; set; }

    public static async Task<BridgeClient> ConnectAsync(
        string pipeName,
        string discoveryToken,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await WriteFrameAsync(
                    pipe,
                    new { kind = "authenticate", token = discoveryToken },
                    cancellationToken)
                .ConfigureAwait(false);
            using var response = await ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Bridge closed during authentication.");
            var root = response.RootElement;
            if (!root.GetProperty("success").GetBoolean())
            {
                throw new UnauthorizedAccessException("Plugin debug bridge authentication failed.");
            }

            return new BridgeClient(pipe, root.GetProperty("sessionToken").GetString()!);
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
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotBoxD",
            "Debug",
            processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        while (true)
        {
            try
            {
                var descriptor = JsonSerializer.Deserialize<PluginDebugBridgeDescriptor>(
                    await File.ReadAllBytesAsync(path, linked.Token).ConfigureAwait(false));
                if (descriptor?.ProcessId == processId)
                {
                    return await ConnectAsync(
                            descriptor.PipeName,
                            descriptor.DiscoveryToken,
                            linked.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // The plugin may still be starting or atomically replacing its descriptor.
            }

            try
            {
                await Task.Delay(50, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No DotBoxD debug bridge appeared for plugin process {processId}.");
            }
        }
    }

    public async ValueTask<JsonElement> SendAsync(
        string kind,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
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
            await WriteAsync(request, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        var envelope = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            command,
            Guid.NewGuid().ToString("N"),
            SessionToken,
            JsonSerializer.SerializeToElement(payload ?? new { }));
        var encoded = PluginDebugProtocol.Encode(envelope, 1024 * 1024);
        var bridge = await SendAsync(
                "exchange",
                new Dictionary<string, object?> { ["payload"] = Convert.ToBase64String(encoded) },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(bridge);
        var remote = PluginDebugProtocol.Decode(
            Convert.FromBase64String(bridge.GetProperty("payload").GetString()!),
            1024 * 1024);
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
            await _readLoop.ConfigureAwait(false);
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
                using var frame = await ReadFrameAsync(_pipe, cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    return;
                }

                var root = frame.RootElement;
                if (root.TryGetProperty("id", out var id) && _pending.TryGetValue(id.GetString()!, out var completion))
                {
                    completion.TrySetResult(root.Clone());
                    continue;
                }

                var kind = root.GetProperty("kind").GetString();
                if (kind == "event")
                {
                    var envelope = PluginDebugProtocol.Decode(
                        Convert.FromBase64String(root.GetProperty("payload").GetString()!),
                        1024 * 1024);
                    if (EventReceiver is { } handler)
                    {
                        await handler(envelope).ConfigureAwait(false);
                    }
                }
                else if (kind == "sourcesChanged" && SourcesChangedReceiver is { } sourcesChanged)
                {
                    _ = Task.Run(async () => await sourcesChanged().ConfigureAwait(false), CancellationToken.None);
                }
            }
        }
        finally
        {
            var exception = new EndOfStreamException("Plugin debug bridge disconnected.");
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private async ValueTask WriteAsync(object message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(_pipe, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async ValueTask WriteFrameAsync(Stream stream, object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, DapJson.Options);
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<JsonDocument?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        var first = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (first == 0)
        {
            return null;
        }

        await stream.ReadExactlyAsync(header.AsMemory(first), cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > 1024 * 1024)
        {
            throw new InvalidDataException("Bridge frame is outside the adapter limit.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }

    private static void EnsureSuccess(JsonElement response)
    {
        if (!response.GetProperty("success").GetBoolean())
        {
            throw new DebugAdapterException("bridgeError", response.GetProperty("error").GetString()!);
        }
    }
}

internal sealed class DebugAdapterException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
