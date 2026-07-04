using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.GeneratedFixtures;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Server;

public sealed class GeneratedSubServiceLifecycleRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_OnGeneratedSubService_ReleasesServerInstance()
    {
        var service = new LifecycleRootService();
        var serializer = new MessagePackRpcSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .ProvideSubServiceLifecycleRoot(service)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();

        var root = client.Get<ISubServiceLifecycleRoot>();
        var child = await root.CreateAsync().WaitAsync(Timeout);

        Assert.Equal(42, await child.PingAsync().WaitAsync(Timeout));

        await child.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.True(service.Child.Disposed);

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
            () => child.PingAsync().WaitAsync(Timeout));
        Assert.Equal(RpcErrorTypes.InstanceNotFound, ex.RemoteExceptionType);
    }

    private sealed class LifecycleRootService : ISubServiceLifecycleRoot
    {
        public LifecycleChildService Child { get; } = new();

        public Task<ISubServiceLifecycleChild> CreateAsync(CancellationToken ct = default) =>
            Task.FromResult<ISubServiceLifecycleChild>(Child);
    }

    private sealed class LifecycleChildService : ISubServiceLifecycleChild
    {
        public bool Disposed { get; private set; }

        public Task<int> PingAsync(CancellationToken ct = default)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(LifecycleChildService));
            }

            return Task.FromResult(42);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
