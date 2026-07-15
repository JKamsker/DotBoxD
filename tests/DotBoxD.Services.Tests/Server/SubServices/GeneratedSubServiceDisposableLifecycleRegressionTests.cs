using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.GeneratedFixtures;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Server.SubServices;

public sealed class GeneratedSubServiceDisposableLifecycleRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Dispose_OnGeneratedDisposableSubService_ReleasesServerInstance()
    {
        var service = new DisposableRootService();
        var serializer = new MessagePackRpcSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .ProvideSubServiceDisposableRoot(service)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();

        var root = client.Get<ISubServiceDisposableRoot>();
        var child = await root.CreateAsync().WaitAsync(Timeout);

        Assert.Equal(42, await child.PingAsync().WaitAsync(Timeout));

        child.Dispose();
        Assert.True(service.Child.Disposed);

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
            () => child.PingAsync().WaitAsync(Timeout));
        Assert.Equal(RpcErrorTypes.InstanceNotFound, ex.RemoteExceptionType);
    }

    private sealed class DisposableRootService : ISubServiceDisposableRoot
    {
        public DisposableChildService Child { get; } = new();

        public Task<ISubServiceDisposableChild> CreateAsync(CancellationToken ct = default) =>
            Task.FromResult<ISubServiceDisposableChild>(Child);
    }

    private sealed class DisposableChildService : ISubServiceDisposableChild
    {
        public bool Disposed { get; private set; }

        public Task<int> PingAsync(CancellationToken ct = default)
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(DisposableChildService));
            }

            return Task.FromResult(42);
        }

        public void Dispose() => Disposed = true;
    }
}
