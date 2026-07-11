using System.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.EndToEndCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class EndToEndBlankInstanceIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankInstanceId_OnInstancePrimitive_FailsBeforeDispatch(string instanceId)
    {
        var (server, client, _) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => client.InvokeOnInstanceAsync<ServerStatus>(
                    "IGameService",
                    instanceId,
                    "GetServerStatusAsync").WaitAsync(EndToEndTimeout));

            Assert.Equal("instanceId", ex.ParamName);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeneratedSubServiceProxy_BlankHandleInstanceId_FailsBeforeDispatch(string instanceId)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer
            .Over(serverConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = EndToEndTimeout })
            .Provide((IServiceDispatcher)new BlankHandleRootDispatcher(instanceId))
            .ProvideSubServiceLifecycleChild(new LifecycleChildService())
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = EndToEndTimeout })
            .Start();

        var root = client.Get<ISubServiceLifecycleRoot>();
        var child = await root.CreateAsync().WaitAsync(EndToEndTimeout);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => child.PingAsync().WaitAsync(EndToEndTimeout));

        Assert.Equal("instanceId", ex.ParamName);
    }

    private sealed class BlankHandleRootDispatcher(string instanceId) : IServiceDispatcher
    {
        public string ServiceName => nameof(ISubServiceLifecycleRoot);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            Assert.Equal(nameof(ISubServiceLifecycleRoot.CreateAsync), method);
            serializer.Serialize(output, new ServiceHandle
            {
                ServiceName = nameof(ISubServiceLifecycleChild),
                InstanceId = instanceId,
            });
            return Task.CompletedTask;
        }
    }

    private sealed class LifecycleChildService : ISubServiceLifecycleChild
    {
        public Task<int> PingAsync(CancellationToken ct = default) => Task.FromResult(42);

        public ValueTask DisposeAsync() => default;
    }
}
