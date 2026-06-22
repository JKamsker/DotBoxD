using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class CoreInternalScenarioCoverageTests
{
    [Fact]
    public async Task CancelFrame_SendThrows_IsSwallowed_AndPeerStaysUsable()
    {
        var serializer = NewSerializer();
        await using var channel = new CancelSendControlChannel(faultCancelSends: true);
        await using var peer = RpcPeer
            .Over(channel, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        using var cts = new CancellationTokenSource();
        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1, cts.Token);

        // Make sure the request frame is on the wire before cancelling, so the cancel-frame path runs.
        await channel.RequestSent.Task.WaitAsync(Timeout5s);
        cts.Cancel();

        // The call still fails with cancellation even though emitting the cancel frame threw internally.
        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Timeout10s));

        // The cancel-frame send was attempted (and threw); the peer remains alive and usable.
        await channel.CancelSendAttempted.Task.WaitAsync(Timeout10s);
        Assert.True(peer.IsConnected);

        // A follow-up call still works (the swallowed fault did not corrupt the peer).
        var followUp = peer.InvokeAsync<int, string>(Service, Method, request: 2);
        channel.EnqueueResponse(serializer, messageId: 2, result: "ok");
        Assert.Equal("ok", await followUp.WaitAsync(Timeout10s));
    }

    // ------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);

    // ------------------------------------------------------------------------------------------
    // Fake transports / channels / dispatchers
    // ------------------------------------------------------------------------------------------

    /// <summary>Server transport whose AcceptAsync always throws a non-cancellation exception and never
    /// returns a connection, so the host accept loop is forced into its perpetual fault/backoff cycle
    /// until its token is cancelled.</summary>
    private sealed class AlwaysFaultServerTransport : IServerTransport
    {
        private int _stopped;

        public bool WasStopped => Volatile.Read(ref _stopped) != 0;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("accept always faults");

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopped, 1);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Server transport whose StartAsync blocks until released, letting a test dispose the host
    /// while a start is in progress.</summary>
    private sealed class GatedStartServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopped;
        private int _disposed;

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasStopped => Volatile.Read(ref _stopped) != 0;

        public bool WasDisposed => Volatile.Read(ref _disposed) != 0;

        public void ReleaseStart() => _release.TrySetResult(true);

        public async Task StartAsync(CancellationToken ct = default)
        {
            StartEntered.TrySetResult(true);
            await _release.Task.ConfigureAwait(false);
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _stopped, 1);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _release.TrySetResult(true);
            return default;
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> whose DisposeAsync throws. Optionally signals a channel close after
    /// the first receive so an accepted peer's read loop ends on its own (driving the natural-disconnect
    /// background-dispose path); otherwise ReceiveAsync parks until disposed.
    /// </summary>
    private sealed class DisposeThrowingChannel : IRpcChannel
    {
        private readonly bool _closeAfterFirstReceive;
        private readonly TaskCompletionSource<bool> _parked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _receiveCalls;
        private int _disposeAttempts;

        public DisposeThrowingChannel(bool closeAfterFirstReceive) =>
            _closeAfterFirstReceive = closeAfterFirstReceive;

        public bool IsConnected => Volatile.Read(ref _disposeAttempts) == 0;

        public string RemoteEndpoint => "dispose-throwing://remote";

        public bool DisposeWasAttempted => Volatile.Read(ref _disposeAttempts) != 0;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            if (_closeAfterFirstReceive && Interlocked.Increment(ref _receiveCalls) == 1)
            {
                // Signal an orderly remote close so the read loop ends and the host disconnects the peer.
                return Payload.Empty;
            }

            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _parked))
            {
                await _parked.Task.ConfigureAwait(false);
            }

            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeAttempts);
            _parked.TrySetResult(true);
            throw new InvalidOperationException("channel dispose failed");
        }
    }

}
