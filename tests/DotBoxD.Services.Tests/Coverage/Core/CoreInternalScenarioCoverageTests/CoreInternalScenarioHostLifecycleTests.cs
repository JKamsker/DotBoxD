using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.CoreInternalScenarioTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Round-2 behavioral coverage that drives specific internal teardown / error / cancellation paths
/// through the public <see cref="RpcHost"/> and <see cref="RpcPeer"/> surface plus purpose-built
/// fake channels and transports. Every scenario asserts an observable outcome (event raised, exception
/// type/message, peer/host state, clean teardown within a bounded timeout). Pure thread-race and
/// genuinely unreachable defensive branches are deliberately left to the test manifest rather than
/// covered with flaky or sleep-based tests.
/// </summary>
public sealed class CoreInternalScenarioHostLifecycleTests
{
    private const string Service = "Svc";
    private const string Method = "Op";
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout10s = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // ------------------------------------------------------------------------------------------
    // 1) RpcHostAcceptLoop: an accept that ALWAYS faults, then the host is stopped. Cancellation
    //    lands during the post-error backoff (or during the next AcceptAsync), so DelayAfterErrorAsync
    //    observes the cancelled token and returns false, breaking the loop (lines 41-43, 113-115).
    //    The existing host test only faults-then-accepts, which never cancels during backoff.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AcceptLoop_AlwaysFaulting_CancelDuringBackoff_ExitsCleanly()
    {
        var transport = new AlwaysFaultServerTransport();
        var firstError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = RpcHost.Listen(transport, NewSerializer());
        host.AcceptError += (_, args) => firstError.TrySetResult(args.Error);
        await host.StartAsync().WaitAsync(Timeout5s);

        // Prove the loop entered its fault/backoff cycle at least once before we cancel it.
        var reported = await firstError.Task.WaitAsync(Timeout5s);
        Assert.IsType<InvalidOperationException>(reported);

        // Stopping cancels the loop's token. Because the transport never returns a connection and never
        // throws OperationCanceledException itself, the only way the loop can terminate is the
        // cancel-during-backoff break — so a clean StopAsync within the timeout proves that path ran.
        await host.StopAsync().WaitAsync(Timeout10s);

        // The host is fully torn down and re-disposable without faulting.
        await host.DisposeAsync().AsTask().WaitAsync(Timeout5s);
        Assert.True(transport.WasStopped);
    }

    // ------------------------------------------------------------------------------------------
    // 2) RpcHost.StartAsync dispose-during-start: the listener's StartAsync is held open until the
    //    host has been concurrently disposed. When StartAsync returns, the post-start lock observes
    //    _disposed != 0 and runs the start-after-dispose cleanup (stops the started listener, throws
    //    ObjectDisposedException) instead of launching the accept loop (RpcHost 118-132, 149-172).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_DisposedWhileListenerStarting_ThrowsObjectDisposed_AndStopsListener()
    {
        var transport = new GatedStartServerTransport();
        var host = RpcHost.Listen(transport, NewSerializer());

        var startTask = host.StartAsync();

        // Wait until StartAsync is genuinely parked inside listener.StartAsync (so _cts/_starting are set
        // but the accept loop has not launched), then dispose the host concurrently.
        await transport.StartEntered.Task.WaitAsync(Timeout5s);
        var disposeTask = host.DisposeAsync().AsTask();

        // Let listener.StartAsync complete; StartAsync now sees the disposed host and unwinds.
        transport.ReleaseStart();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => startTask.WaitAsync(Timeout10s));
        await disposeTask.WaitAsync(Timeout10s);

        // Cleanup stopped the listener it had started, and the listener was disposed by DisposeAsync.
        Assert.True(transport.WasStopped);
        Assert.True(transport.WasDisposed);
    }

    // ------------------------------------------------------------------------------------------
    // 3) RpcHostPeerCollection.CloseAllAsync: a peer whose channel dispose THROWS must be swallowed by
    //    DisposeOnePeerAsync's best-effort catch (lines 50-59) so host stop still completes.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task HostStop_PeerDisposeThrows_IsSwallowed_AndStopCompletes()
    {
        // Server side never closes on its own (ReceiveAsync parks) and throws on dispose, so the only
        // disposer is the host's CloseAllAsync during StopAsync.
        var serverChannel = new DisposeThrowingChannel(closeAfterFirstReceive: false);
        var transport = new SingleConnectionServerTransport(serverChannel);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.Provide((IServiceDispatcher)new NoopDispatcher()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        var peer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(peer.IsConnected);

        // CloseAllAsync disposes the tracked peer; the peer's channel dispose throws, which
        // DisposeOnePeerAsync swallows so StopAsync still returns cleanly.
        await host.StopAsync().WaitAsync(Timeout10s);
        Assert.True(serverChannel.DisposeWasAttempted);
    }

    // ------------------------------------------------------------------------------------------
    // 3b) RpcHostPeerCollection.DisposeInBackground + AwaitCleanupAsync: a peer that disconnects
    //     naturally is disposed off the read-loop callback; its channel dispose throws, exercising the
    //     background-dispose catch (22-25) and the AwaitCleanupAsync best-effort catch (74-77).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task PeerNaturalDisconnect_BackgroundDisposeThrows_IsSwallowed_AndHostStopsCleanly()
    {
        // The server channel returns one frame attempt then signals close (Payload.Empty), so the
        // accepted peer's read loop ends on its own -> OnPeerDisconnected -> DisposeInBackground.
        var serverChannel = new DisposeThrowingChannel(closeAfterFirstReceive: true);
        var transport = new SingleConnectionServerTransport(serverChannel);
        var disconnected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.Provide((IServiceDispatcher)new NoopDispatcher()));
        host.PeerDisconnected += (_, args) => disconnected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        // The accepted peer disconnects as soon as the read loop sees the close signal.
        var peer = await disconnected.Task.WaitAsync(Timeout10s);
        Assert.NotNull(peer);

        // StopAsync awaits the background cleanup task; the peer dispose faulted, so AwaitCleanupAsync's
        // catch swallows it and stop still completes within the timeout.
        await host.StopAsync().WaitAsync(Timeout10s);
        Assert.True(serverChannel.DisposeWasAttempted);
    }

    // ------------------------------------------------------------------------------------------
    // 4) RpcPeerInboundDispatcher.SendQueueFullErrorAsync: when a request is shed under DropIncoming
    //    backpressure AND the queue-full error frame cannot be sent, the best-effort catch swallows the
    //    send fault (lines 117-125) without tearing down the peer.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task QueueFull_WhenErrorSendFails_SwallowsFault_AndPeerStaysAlive()
    {
        var serializer = NewSerializer();
        await using var connection = new SendFailingScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        // Capacity 1 + a blocking dispatcher: the first request occupies the slot, the overflow request
        // is dropped and the dispatcher tries to send a QueueFull error, which this channel fails.
        connection.Enqueue(CreateRequestFrame(serializer, 1, BlockingDispatcher.Service, "Hold"));
        connection.Enqueue(CreateRequestFrame(serializer, 2, BlockingDispatcher.Service, "Hold"));
        connection.Enqueue(CreateRequestFrame(serializer, 3, BlockingDispatcher.Service, "Hold"));

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    MaxInboundBytes = null,
                    QueueFullMode = QueueFullMode.DropIncoming,
                    RequestTimeout = Timeout5s,
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(Timeout5s);

        // The dispatcher attempted (and failed) to send at least one QueueFull error frame for an
        // overflow request; the failure was swallowed so the peer never tore down.
        await connection.WaitForSendAttemptsAsync(1, Timeout10s);
        Assert.True(peer.IsConnected);

        dispatcher.Release();
    }

    // ------------------------------------------------------------------------------------------
    // 6a) RpcPeerCancelFrameSender.SendAsync: when emitting a cancel frame fails, the exception is
    //     reported and swallowed (lines 90-93) — it never faults the outbound call or the peer.
    // ------------------------------------------------------------------------------------------

}
