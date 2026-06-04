using System.IO.Pipes;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.NamedPipes;

/// <summary>
/// Server transport for accepting ShaRPC connections over a named pipe.
/// </summary>
public sealed class NamedPipeServerTransport : IServerTransport
{
    /// <summary>
    /// Default inter-read idle timeout applied to accepted connections' in-progress frame body reads
    /// (30 seconds). Mirrors <c>TcpConnection.DefaultFrameReadIdleTimeout</c> so accepted pipe
    /// connections get the same finite slow-loris defense the TCP transport applies by default.
    /// </summary>
    public static readonly TimeSpan DefaultFrameReadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly string _pipeName;
    private readonly int _maxAllowedServerInstances;
    private readonly int _maxMessageSize;
    private CancellationTokenSource? _stopCts;
    private NamedPipeServerStream? _pendingStream;
    private int _started;
    private int _disposed;

    public NamedPipeServerTransport(
        string pipeName,
        int maxAllowedServerInstances = NamedPipeServerStream.MaxAllowedServerInstances,
        int maxMessageSize = MessageFramer.MaxMessageSize)
    {
        _pipeName = ValidatePipeName(pipeName);
        _maxAllowedServerInstances = ValidateMaxAllowedServerInstances(maxAllowedServerInstances);
        _maxMessageSize = ValidateMaxMessageSize(maxMessageSize);
    }

    /// <summary>
    /// Inter-read idle timeout applied to accepted connections' in-progress frame body reads (slow-loris
    /// defense). <see langword="null"/> uses <see cref="DefaultFrameReadIdleTimeout"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it. See <see cref="StreamConnection"/>.
    /// </summary>
    public TimeSpan? FrameReadIdleTimeout { get; init; }

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Server already started.");
        }

        _stopCts = new CancellationTokenSource();
        return Task.CompletedTask;
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var stream = CreateStream();
        CancellationTokenSource linkedCts;
        try
        {
            // Take _sync to register the pending stream AND build the stop-linked token together, so a
            // concurrent StopAsync — which nulls and disposes _stopCts under the same lock — cannot slip
            // between the started/stop-source check and the _stopCts.Token read (which would otherwise
            // throw NullReference/ObjectDisposed instead of cancelling cleanly).
            lock (_sync)
            {
                if (Volatile.Read(ref _started) == 0 || _stopCts is null)
                {
                    throw new InvalidOperationException("Server not started.");
                }

                if (_pendingStream is not null)
                {
                    throw new InvalidOperationException("Only one pending named-pipe accept is supported per transport.");
                }

                _pendingStream = stream;
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token);
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        try
        {
            using (linkedCts)
            {
                await WaitForConnectionAsync(stream, linkedCts.Token).ConfigureAwait(false);

                // Test seam (null/no-op in production): lets a test deterministically interleave StopAsync
                // (which disposes _pendingStream) between WaitForConnectionAsync returning and the
                // StreamConnection construction below. Never set in production.
                var connectedHook = _onConnectionEstablishedForTest;
                if (connectedHook is not null)
                {
                    await connectedHook().ConfigureAwait(false);
                }

                // Re-check after the wait (and the test seam) returns: a StopAsync that raced in here
                // disposed _pendingStream and cancelled the linked token, so constructing a channel now
                // would hand back a connection over an already-disposed pipe. Surface the stop as
                // cancellation instead.
                if (linkedCts.IsCancellationRequested || !stream.IsConnected)
                {
                    stream.Dispose();
                    throw new OperationCanceledException(linkedCts.Token);
                }

                return new StreamConnection(
                    stream,
                    $"pipe://./{_pipeName}",
                    ownsStream: true,
                    _maxMessageSize,
                    FrameReadIdleTimeout ?? DefaultFrameReadIdleTimeout);
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        finally
        {
            ClearPendingStream(stream);
        }
    }

    /// <summary>
    /// Test-only seam invoked once after <c>WaitForConnectionAsync</c> returns and before the
    /// <see cref="StreamConnection"/> is constructed, so a test can deterministically interleave
    /// <see cref="StopAsync"/> (which disposes the pending stream) there. Never set in production.
    /// </summary>
    internal Func<Task>? _onConnectionEstablishedForTest;

    public Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return Task.CompletedTask;
        }

        // Null and capture _stopCts under _sync (the same lock AcceptAsync reads it under) and dispose
        // the pending stream, then cancel+dispose the captured source outside the lock. AcceptAsync
        // that runs after this sees _stopCts == null and fails fast with "not started"; one already
        // inside the lock captured a live linked token that the Cancel below still fires.
        CancellationTokenSource? stopCts;
        lock (_sync)
        {
            stopCts = _stopCts;
            _stopCts = null;
            _pendingStream?.Dispose();
            _pendingStream = null;
        }

        // Test seam (null/no-op in production): invoked after the pending stream has been disposed but
        // before the stop source is cancelled, so a test can deterministically open the dispose-before-cancel
        // window and observe how a blocked AcceptAsync surfaces the disposed stream. Never set in production.
        var beforeCancelHook = _beforeStopCancelForTest;
        if (beforeCancelHook is not null)
        {
            return StopWithBeforeCancelHookAsync(beforeCancelHook, stopCts);
        }

        if (stopCts is not null)
        {
            stopCts.Cancel();
            stopCts.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Test-only seam invoked inside <see cref="StopAsync"/> after the pending stream is disposed and
    /// before <c>stopCts.Cancel()</c> runs. Lets a test deterministically widen the dispose-before-cancel
    /// window so a blocked <c>AcceptAsync</c> can observe the disposed stream before cancellation is
    /// signalled. Never set in production.
    /// </summary>
    internal Func<Task>? _beforeStopCancelForTest;

    private static async Task StopWithBeforeCancelHookAsync(Func<Task> beforeCancelHook, CancellationTokenSource? stopCts)
    {
        await beforeCancelHook().ConfigureAwait(false);

        if (stopCts is not null)
        {
            stopCts.Cancel();
            stopCts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    private NamedPipeServerStream CreateStream() =>
        new(
            _pipeName,
            PipeDirection.InOut,
            _maxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    private void ClearPendingStream(NamedPipeServerStream stream)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_pendingStream, stream))
            {
                _pendingStream = null;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NamedPipeServerTransport));
        }
    }

    private static async Task WaitForConnectionAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            using (ct.Register(static state => ((NamedPipeServerStream)state!).Dispose(), stream))
            {
                await stream.WaitForConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (IOException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
    }

    private static string ValidatePipeName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name cannot be null, empty, or whitespace.", nameof(pipeName));
        }

        return pipeName;
    }

    private static int ValidateMaxAllowedServerInstances(int value)
    {
        if (value != NamedPipeServerStream.MaxAllowedServerInstances && value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Maximum server instances must be positive.");
        }

        return value;
    }

    private static int ValidateMaxMessageSize(int value)
    {
        if (value < MessageFramer.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Maximum message size must be at least the ShaRPC header size.");
        }

        return value;
    }
}
