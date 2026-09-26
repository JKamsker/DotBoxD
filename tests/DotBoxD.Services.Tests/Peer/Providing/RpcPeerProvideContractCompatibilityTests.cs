using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Providing;

public sealed class RpcPeerProvideContractCompatibilityTests
{
    static RpcPeerProvideContractCompatibilityTests()
    {
        GeneratedServiceRegistry.Register<IProvideContract>(
            _ => new DualRoleProvideDispatcher(),
            value => ((DualRoleProvideDispatcher)value).CreateContractDispatcher());
        GeneratedServiceRegistry.Register<IProvideDispatcherContract>(
            _ => new DualRoleProvideDispatcher(),
            value => ((DualRoleProvideDispatcher)value).CreateContractDispatcher());
    }

    [Theory]
    [InlineData("Explicit", false)]
    [InlineData("Explicit", true)]
    [InlineData("Inferred", false)]
    [InlineData("Inferred", true)]
    [InlineData("Provider", false)]
    [InlineData("Provider", true)]
    public async Task InterfaceContract_StillUsesRegisteredFactory(string mode, bool dispatcherContract)
    {
        var implementation = new DualRoleProvideDispatcher();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        await using var client = RpcPeer.Over(clientConnection, serializer,
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        await using var server = RpcPeer.Over(serverConnection, serializer,
            new RpcPeerOptions { ServiceProvider = new ProvideServiceProvider(implementation) });

        Assert.Same(server, Provide(server, implementation, mode, dispatcherContract));
        server.Start();
        client.Start();

        Assert.Equal(7, await client.InvokeAsync<int>("Contract", "Read"));
        Assert.Equal(1, implementation.FactoryCalls);
        Assert.Equal(0, implementation.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterfaceContract_DoesNotFallBackWhenFactoryThrows(bool dispatcherContract)
    {
        var expected = new InvalidOperationException("Factory failed");
        var implementation = new DualRoleProvideDispatcher { FactoryError = expected };
        await using var peer = RpcPeer.Over(new ScriptedConnection(), new MessagePackRpcSerializer());

        var actual = Assert.Throws<InvalidOperationException>(() => Provide(peer, implementation, "Explicit", dispatcherContract));

        Assert.Same(expected, actual);
        Assert.Equal(1, implementation.FactoryCalls);
        Assert.Equal(0, implementation.Calls);
    }

    private static RpcPeer Provide(RpcPeer peer, DualRoleProvideDispatcher implementation,
        string mode, bool dispatcherContract) => mode switch
        {
            "Explicit" => dispatcherContract
                ? peer.Provide<IProvideDispatcherContract>(implementation) : peer.Provide<IProvideContract>(implementation),
            "Inferred" => dispatcherContract
                ? peer.Provide((IProvideDispatcherContract)implementation) : peer.Provide((IProvideContract)implementation),
            "Provider" => dispatcherContract ? peer.Provide<IProvideDispatcherContract>() : peer.Provide<IProvideContract>(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
}
