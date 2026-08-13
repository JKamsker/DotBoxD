using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class RpcHostRejectedPeerCleanupFailureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task RejectedPeerCleanupFailure_DoesNotRetryChannelDisposalOrRaiseSecondAcceptError()
    {
        var configurationFailure = new InvalidOperationException("configuration sentinel");
        var channel = new ThrowingDisposeChannel();
        var firstAcceptError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAcceptError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptErrorCount = 0;
        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(channel), new MessagePackRpcSerializer())
            .ForEachPeer(_ => throw configurationFailure);

        host.AcceptError += (_, args) =>
        {
            if (Interlocked.Increment(ref acceptErrorCount) == 1)
            {
                firstAcceptError.TrySetResult(args.Error);
            }
            else
            {
                secondAcceptError.TrySetResult(args.Error);
            }
        };

        await host.StartAsync();
        await channel.FirstDisposalAttempted.WaitAsync(Timeout);

        Assert.Same(configurationFailure, await firstAcceptError.Task.WaitAsync(Timeout));
        await Assert.ThrowsAsync<TimeoutException>(() => secondAcceptError.Task.WaitAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, channel.DisposeAttempts);
        Assert.Equal(1, Volatile.Read(ref acceptErrorCount));
    }

    private sealed class ThrowingDisposeChannel : IRpcChannel
    {
        private readonly TaskCompletionSource _firstDisposalAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeAttempts;

        public Task FirstDisposalAttempted => _firstDisposalAttempted.Task;

        public int DisposeAttempts => Volatile.Read(ref _disposeAttempts);

        public bool IsConnected => true;

        public string RemoteEndpoint => "test://rejected-peer-cleanup";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromException<Payload>(new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeAttempts);
            _firstDisposalAttempted.TrySetResult();
            return ValueTask.FromException(new InvalidOperationException("cleanup sentinel"));
        }
    }
}
