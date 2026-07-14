using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Kernels;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Pushdown.Services;

/// <summary>
/// Opt-in plugin-side bridge between one local DAP adapter and the existing remote debug byte services.
/// The local endpoint uses a high-entropy name, a separate discovery token, and current-user-only pipe access.
/// </summary>
public sealed class PluginDebugBridge : IPluginDebugEventRpcService, IAsyncDisposable
{
    private readonly PluginDebugBridgeOptions _options;
    private readonly PluginDebugRemoteConnection _remote;
    private readonly PluginDebugSourceCatalog _sources;
    private readonly PluginDebugBridgeRequestHandler _requests;
    private readonly PluginDebugSourceRefreshTracker _sourceRefreshes = new();
    private readonly int _localFrameBytes;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<LocalBridgeEvent> _events = Channel.CreateUnbounded<LocalBridgeEvent>();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly TaskCompletionSource<string> _sessionToken =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _configured =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _runTask;
    private int _disposed;

    private PluginDebugBridge(PluginDebugBridgeOptions options)
    {
        _options = options;
        _localFrameBytes = PluginDebugBridgeProtocol.WrappedEnvelopeLimit(options.MaxFrameBytes);
        _remote = new PluginDebugRemoteConnection(options.MaxFrameBytes);
        var pipeName = "dotboxd-debug-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        Descriptor = PluginDebugBridgeDiscovery.Publish(
            new PluginDebugBridgeDescriptor(Environment.ProcessId, pipeName, token));
        _sources = new PluginDebugSourceCatalog(options.SourceReader ?? PluginDebugBridgeDiscovery.ReadSource);
        _requests = new PluginDebugBridgeRequestHandler(
            _remote.ExchangeAsync,
            _sources,
            MarkConfigured,
            _sourceRefreshes.Acknowledge);
        _runTask = RunAsync(_lifetime.Token);
    }

    public PluginDebugBridgeDescriptor Descriptor { get; }

    /// <summary>Starts the local listener. Calling this method is the explicit launch-tooling opt-in.</summary>
    public static PluginDebugBridge Start(PluginDebugBridgeOptions? options = null)
    {
        options ??= new PluginDebugBridgeOptions();
        options.Validate();
        return new PluginDebugBridge(options);
    }

    /// <summary>Attaches the remote control proxy after the plugin's existing peer has connected.</summary>
    public void AttachControl(IPluginDebugControlRpcService control)
    {
        _remote.Attach(control);
    }

    /// <summary>Registers client-only source maps before installation.</summary>
    public void RegisterPackage(PluginPackage package)
        => _ = RegisterPackageCore(package);

    private long RegisterPackageCore(PluginPackage package)
    {
        _sources.Register(package);
        var version = _sourceRefreshes.Register();
        _events.Writer.TryWrite(LocalBridgeEvent.SourcesChanged(version));
        return version;
    }

    /// <summary>Registers package maps and waits until the adapter has sent configurationDone.</summary>
    public async ValueTask PreparePackageAsync(
        PluginPackage package,
        CancellationToken cancellationToken = default)
    {
        var sourceVersion = RegisterPackageCore(package);
        await WaitForSourceRefreshAsync(sourceVersion, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers maps, waits for DAP configuration when requested, then installs the kernel.</summary>
    public async ValueTask<InstalledKernel> InstallAsync(
        PluginSession session,
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var sourceVersion = RegisterPackageCore(package);
        await WaitForSourceRefreshAsync(sourceVersion, cancellationToken).ConfigureAwait(false);
        return await session.InstallAsync(package, policy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers maps, waits for DAP configuration when requested, then installs a server extension.</summary>
    public async ValueTask<InstalledKernel> InstallServerExtensionAsync(
        PluginSession session,
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var sourceVersion = RegisterPackageCore(package);
        await WaitForSourceRefreshAsync(sourceVersion, cancellationToken).ConfigureAwait(false);
        return await session.InstallServerExtensionAsync(package, policy, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        CaptureBootstrap(message);
        return _events.Writer.WriteAsync(LocalBridgeEvent.Remote(message.ToArray()), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _events.Writer.TryComplete();
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected listener shutdown.
        }

        _writeGate.Dispose();
        PluginDebugBridgeDiscovery.Remove(Descriptor);
        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(
                Descriptor.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ServeAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or JsonException or ArgumentException or TimeoutException)
            {
                // A premature, malformed, or abruptly closed local client cannot terminate the protected listener.
            }
        }
    }

    private async Task ServeAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var authentication = await PluginDebugBridgeProtocol.ReadAsync(
                stream,
                _localFrameBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (!Authenticate(authentication?.RootElement))
        {
            await WriteAsync(stream, new { kind = "authentication", success = false }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var sessionToken = await _sessionToken.Task
            .WaitAsync(_options.DebuggerWaitTimeout, cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(
                stream,
                new { kind = "authentication", success = true, sessionToken, protocolVersion = PluginDebugProtocol.Version, maxFrameBytes = _options.MaxFrameBytes },
                cancellationToken)
            .ConfigureAwait(false);
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = SendEventsAsync(stream, connection.Token);
        try
        {
            await ReceiveRequestsAsync(stream, connection.Token).ConfigureAwait(false);
        }
        finally
        {
            connection.Cancel();
            await PluginDebugBridgeProtocol.IgnoreCancellationAsync(events).ConfigureAwait(false);
            await _remote.DisconnectAsync(sessionToken).ConfigureAwait(false);
        }
    }

    private async Task ReceiveRequestsAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var request = await PluginDebugBridgeProtocol.ReadAsync(
                    stream,
                    _localFrameBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var response = await _requests.HandleAsync(request.RootElement, cancellationToken).ConfigureAwait(false);
            await WriteAsync(stream, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private void MarkConfigured() => _configured.TrySetResult();

    private async Task SendEventsAsync(Stream stream, CancellationToken cancellationToken)
    {
        await foreach (var bridgeEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await WriteAsync(stream, bridgeEvent.Message, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask WriteAsync(Stream stream, object message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PluginDebugBridgeProtocol.WriteAsync(stream, message, _localFrameBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void CaptureBootstrap(byte[] message)
    {
        try
        {
            var envelope = PluginDebugProtocol.Decode(message, _options.MaxFrameBytes);
            if (envelope.Kind == "session")
            {
                _sessionToken.TrySetResult(envelope.SessionToken);
            }
        }
        catch (PluginDebugProtocolException)
        {
            // The server remains authoritative; malformed remote events are forwarded for adapter diagnostics.
        }
    }

    private ValueTask WaitForSourceRefreshAsync(long sourceVersion, CancellationToken cancellationToken)
        => !_options.WaitForDebuggerBeforeInstall
            ? ValueTask.CompletedTask
            : _sourceRefreshes.WaitForConfigurationAsync(
                _configured.Task,
                sourceVersion,
                _options.DebuggerWaitTimeout,
                cancellationToken);

    private bool Authenticate(JsonElement? request)
    {
        if (request is not { ValueKind: JsonValueKind.Object } ||
            !TryReadString(request.Value, "kind", out var kind) ||
            !TryReadString(request.Value, "token", out var token) ||
            !string.Equals(kind, "authenticate", StringComparison.Ordinal))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(token),
            System.Text.Encoding.UTF8.GetBytes(Descriptor.DiscoveryToken));
    }

    private static bool TryReadString(JsonElement request, string name, out string value)
    {
        value = string.Empty;
        if (!request.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

}
