using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerDisposeReentrancyTests
{
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    [Fact]
    public async Task DisposeAsync_WhenDiagnosticHandlerReenters_PublishesOneSharedTerminalBeforeTeardown()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var sentinel = new InvalidOperationException("channel dispose failed");
            var channel = new ThrowingDisposeChannel(sentinel);
            var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());
            Task? reentrantDispose = null;
            var diagnosticCount = 0;

            void OnError(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (!ReferenceEquals(args.Error, sentinel))
                {
                    return;
                }

                if (Interlocked.Increment(ref diagnosticCount) == 1)
                {
                    reentrantDispose = peer.DisposeAsync().AsTask();
                }
            }

            RpcDiagnostics.Error += OnError;
            try
            {
                var initialDispose = peer.DisposeAsync().AsTask();

                var initialError = await Assert.ThrowsAsync<InvalidOperationException>(() => initialDispose);
                var reentrantTask = Assert.IsAssignableFrom<Task>(reentrantDispose);
                var reentrantError = await Assert.ThrowsAsync<InvalidOperationException>(() => reentrantTask);

                Assert.Same(sentinel, initialError);
                Assert.Same(sentinel, reentrantError);
                Assert.Equal(1, channel.DisposeCount);
                Assert.Equal(1, Volatile.Read(ref diagnosticCount));
            }
            finally
            {
                RpcDiagnostics.Error -= OnError;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    private sealed class ThrowingDisposeChannel(InvalidOperationException failure) : IRpcChannel
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool IsConnected => true;

        public string RemoteEndpoint => "throwing-dispose://remote";

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            throw failure;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Payload.Empty);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
