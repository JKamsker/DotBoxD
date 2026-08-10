using System.Runtime.ExceptionServices;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer.Inbound;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer;

public sealed partial class RpcPeer
{
    [ThreadStatic]
    private static RpcPeer? s_disconnectedEventPeer;

    /// <summary>Begins the read loop. Idempotent; safe to call from a fluent chain.</summary>
    public RpcPeer Start()
    {
        EnsureStarted();
        return this;
    }

    private void EnsureStarted()
    {
        if (Volatile.Read(ref _startCompleted) != 0 &&
            Volatile.Read(ref _disposed) == 0 &&
            Volatile.Read(ref _closed) == 0)
        {
            return;
        }

        EnsureStartedSlow();
    }

    private void EnsureStartedSlow()
    {
        lock (_lifecycleLock)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(RpcPeer));
            }

            if (_closed != 0)
            {
                throw new ServiceConnectionException("Connection closed.");
            }

            if (_started != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _started, 1);
            _cts = new CancellationTokenSource();
            _inbound.Start(_cts.Token);
            _readLoop = Task.Run(() => _readLoopRunner.RunAsync(_cts.Token));
            RpcTelemetry.PeerStarted();
            Volatile.Write(ref _startCompleted, 1);
        }
    }

    /// <summary>Closes the peer by disposing it; closed peers cannot be restarted.</summary>
    /// <remarks>
    /// Disposal always runs to completion: <paramref name="ct"/> fails fast only before any teardown
    /// begins, and never abandons an in-progress dispose to finish in the background.
    /// </remarks>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        Task? disposeTask;
        lock (_lifecycleLock)
        {
            disposeTask = _disposeTask;
        }

        if (disposeTask is null)
        {
            ct.ThrowIfCancellationRequested();
            disposeTask = DisposeAsync().AsTask();
        }

        await disposeTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Task? readLoop;
        CancellationTokenSource? cts;
        Task disposeTask;
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = 1;
            Interlocked.Exchange(ref _closed, 1);
            _proxyCache = null;
            cts = _cts;
            readLoop = ReferenceEquals(s_disconnectedEventPeer, this) ? null : _readLoop;
            cts?.Cancel();
            disposeTask = DisposeCoreAsync(readLoop, cts);
            _disposeTask = disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(Task? readLoop, CancellationTokenSource? cts)
    {
        _outbound.FailPending(new ServiceConnectionException("Connection closed."));
        var teardownFailure = await CaptureTeardownFailureAsync(
            async () => await _channel.DisposeAsync().ConfigureAwait(false),
            "Channel dispose during peer teardown failed",
            firstFailure: null).ConfigureAwait(false);

        if (readLoop is not null)
        {
            if (ShouldAwaitReadLoop(readLoop))
            {
                try
                {
                    await readLoop.ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort shutdown.
                }
            }
            else
            {
                ObserveFaultedReadLoop(readLoop);
            }
        }

        teardownFailure = await CaptureTeardownFailureAsync(
            _outbound.StopCancelFramesAsync,
            "Outbound shutdown during peer teardown failed",
            teardownFailure).ConfigureAwait(false);
        teardownFailure = CaptureTeardownFailure(
            _streams.Stop,
            "Stream shutdown during peer teardown failed",
            teardownFailure);
        teardownFailure = await CaptureTeardownFailureAsync(
            _inbound.StopAsync,
            "Inbound shutdown during peer teardown failed",
            teardownFailure).ConfigureAwait(false);
        teardownFailure = CaptureTeardownFailure(
            _sender.Dispose,
            "Sender dispose during peer teardown failed",
            teardownFailure);
        if (cts is not null)
        {
            teardownFailure = CaptureTeardownFailure(
                cts.Dispose,
                "Cancellation source dispose during peer teardown failed",
                teardownFailure);
        }

        if (readLoop is not null)
        {
            teardownFailure = CaptureTeardownFailure(
                RpcTelemetry.PeerStopped,
                "Peer stopped telemetry during peer teardown failed",
                teardownFailure);
        }

        if (teardownFailure is not null)
        {
            ExceptionDispatchInfo.Capture(teardownFailure).Throw();
        }
    }

    private static async Task<Exception?> CaptureTeardownFailureAsync(
        Func<Task> teardown,
        string diagnostic,
        Exception? firstFailure)
    {
        try
        {
            await teardown().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report(diagnostic, ex);
            return firstFailure ?? ex;
        }

        return firstFailure;
    }

    private static Exception? CaptureTeardownFailure(
        Action teardown,
        string diagnostic,
        Exception? firstFailure)
    {
        try
        {
            teardown();
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report(diagnostic, ex);
            return firstFailure ?? ex;
        }

        return firstFailure;
    }

    private bool ShouldAwaitReadLoop(Task readLoop) =>
        readLoop.IsCompleted ||
        _channel is not StreamConnection { OwnsStream: false, HasActiveReceive: true };

    private static void ObserveFaultedReadLoop(Task readLoop)
    {
        _ = readLoop.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RaiseProtocolError(
        int messageId,
        MessageType messageType,
        string message,
        Exception? error)
    {
        RpcTelemetry.ProtocolFrameRejected(messageType, serializationFailure: error is not null);
        RpcEventHandlerInvoker.Raise(
            ProtocolError,
            this,
            new RpcProtocolErrorEventArgs(_channel.RemoteEndpoint, messageId, messageType, message, error));
    }

    private void RaiseDispatchError(RpcPeerInboundRequest inbound, Exception error) =>
        RpcEventHandlerInvoker.Raise(
            DispatchError,
            this,
            new RpcDispatchErrorEventArgs(
                _channel.RemoteEndpoint,
                inbound.MessageId,
                inbound.Request.ServiceName,
                inbound.Request.MethodName,
                inbound.Request.InstanceId,
                error));

    private void MarkClosed() => Volatile.Write(ref _closed, 1);

    private void RaiseReadError(Exception error) =>
        RpcEventHandlerInvoker.Raise(
            ReadError,
            this,
            new RpcReadErrorEventArgs(_channel.RemoteEndpoint, error));

    private void RaiseDisconnected(Exception? error)
    {
        var previousPeer = s_disconnectedEventPeer;
        s_disconnectedEventPeer = this;
        try
        {
            RpcEventHandlerInvoker.Raise(
                Disconnected,
                this,
                new RpcDisconnectedEventArgs(_channel.RemoteEndpoint, error));
        }
        finally
        {
            s_disconnectedEventPeer = previousPeer;
        }
    }
}
