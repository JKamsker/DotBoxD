using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerSessionDisposeReentrancyTests
{
    [Fact]
    public async Task DisposeAsync_WhenTransportFailureReportsDiagnostic_ReusesOriginalTeardown()
    {
        var channelFailure = new InvalidOperationException("channel disposal failed");
        var transportFailure = new InvalidOperationException("transport disposal failed");
        var channel = new ThrowingDisposeChannel(channelFailure);
        var transport = new ThrowingDisposeTransport(channel, transportFailure);
        var session = await RpcPeerSession.ConnectAsync(transport, new MessagePackRpcSerializer());
        var reentrancyStarted = 0;
        Task? reentrantDispose = null;
        EventHandler<RpcDiagnosticErrorEventArgs>? handler = (_, args) =>
        {
            if (args.Operation == "Transport dispose during peer session teardown failed" &&
                Interlocked.CompareExchange(ref reentrancyStarted, 1, 0) == 0)
            {
                reentrantDispose = session.DisposeAsync().AsTask();
            }
        };

        RpcDiagnostics.Error += handler;
        try
        {
            var outerDispose = session.DisposeAsync().AsTask();

            var outerFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => outerDispose);
            var reentrantFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => reentrantDispose ?? throw new InvalidOperationException("Reentrant dispose was not called."));

            Assert.Same(channelFailure, outerFailure);
            Assert.Same(channelFailure, reentrantFailure);
            Assert.Equal(1, channel.DisposeCount);
            Assert.Equal(1, transport.DisposeCount);
            Assert.Equal(1, reentrancyStarted);
        }
        finally
        {
            RpcDiagnostics.Error -= handler;
        }
    }

    private sealed class ThrowingDisposeTransport(IRpcChannel channel, InvalidOperationException failure) : ITransport
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public IRpcChannel? Connection { get; private set; }

        public bool IsConnected => Connection?.IsConnected == true;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Connection = channel;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            throw failure;
        }
    }

    private sealed class ThrowingDisposeChannel(InvalidOperationException failure) : IRpcChannel
    {
        private readonly TaskCompletionSource<bool> _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool IsConnected => Volatile.Read(ref _disposeCount) == 0;

        public string RemoteEndpoint => "throwing-dispose://session";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            await _disposed.Task.WaitAsync(ct);
            return Payload.Empty;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _disposed.TrySetResult(true);
            throw failure;
        }
    }
}
