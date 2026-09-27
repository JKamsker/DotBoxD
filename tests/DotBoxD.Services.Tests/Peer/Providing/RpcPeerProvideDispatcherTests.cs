using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Providing;

public sealed class RpcPeerProvideDispatcherTests
{
    [Theory]
    [InlineData("Concrete", false)]
    [InlineData("Concrete", true)]
    [InlineData("Object", false)]
    [InlineData("Object", true)]
    [InlineData("ExplicitConcrete", false)]
    [InlineData("ExplicitConcrete", true)]
    [InlineData("Provider", false)]
    [InlineData("Provider", true)]
    [InlineData("GenericHelper", false)]
    [InlineData("GenericHelper", true)]
    [InlineData("Interface", false)]
    [InlineData("Interface", true)]
    public async Task HandwrittenDispatcher_CanBeProvidedAndCalled(string mode, bool dualRole)
    {
        ManualProvideDispatcher dispatcher = dualRole ? new DualRoleProvideDispatcher() : new ManualProvideDispatcher();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        await using var client = RpcPeer.Over(clientConnection, serializer,
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        await using var server = RpcPeer.Over(serverConnection, serializer,
            new RpcPeerOptions { ServiceProvider = new ProvideServiceProvider(dispatcher) });

        Assert.Same(server, Provide(server, dispatcher, mode));
        server.Start();
        client.Start();

        Assert.Equal(42, await client.InvokeAsync<int>("Manual", "Read"));
        Assert.Equal(1, dispatcher.Calls);
        if (dispatcher is DualRoleProvideDispatcher dual)
        {
            Assert.Equal(0, dual.FactoryCalls);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HandwrittenDispatcher_PreservesLifecycleGuards(bool disposed, bool fromProvider)
    {
        var dispatcher = new ManualProvideDispatcher();
        await using var peer = RpcPeer.Over(new ScriptedConnection(), new MessagePackRpcSerializer(),
            new RpcPeerOptions { ServiceProvider = new ProvideServiceProvider(dispatcher) });
        if (disposed)
        {
            await peer.DisposeAsync();
        }
        else
        {
            peer.Start();
        }

        void ProvideDispatcher()
        {
            if (fromProvider)
                peer.Provide<ManualProvideDispatcher>();
            else
                peer.Provide(dispatcher);
        }

        if (disposed)
            Assert.Throws<ObjectDisposedException>(ProvideDispatcher);
        else
            Assert.Throws<InvalidOperationException>(ProvideDispatcher);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandwrittenDispatcher_PreservesNullValidation(bool explicitGeneric)
    {
        await using var peer = RpcPeer.Over(new ScriptedConnection(), new MessagePackRpcSerializer());
        ManualProvideDispatcher dispatcher = null!;
        var error = explicitGeneric
            ? Assert.Throws<ArgumentNullException>(() => peer.Provide<ManualProvideDispatcher>(dispatcher))
            : Assert.Throws<ArgumentNullException>(() => peer.Provide(dispatcher));
        Assert.Equal("implementation", error.ParamName);
    }

    [Fact]
    public async Task OrdinaryConcreteService_StillRequiresAnInterface()
    {
        await using var peer = RpcPeer.Over(new ScriptedConnection(), new MessagePackRpcSerializer());
        var error = Assert.Throws<ArgumentException>(() => peer.Provide(new object()));
        Assert.Equal("serviceInterface", error.ParamName);
    }

    private static RpcPeer Provide(RpcPeer peer, ManualProvideDispatcher dispatcher, string mode) => mode switch
    {
        "Concrete" => dispatcher is DualRoleProvideDispatcher dual ? peer.Provide(dual) : peer.Provide(dispatcher),
        "Object" => peer.Provide((object)dispatcher),
        "ExplicitConcrete" => dispatcher is DualRoleProvideDispatcher dual
            ? peer.Provide<DualRoleProvideDispatcher>(dual) : peer.Provide<ManualProvideDispatcher>(dispatcher),
        "Provider" => dispatcher is DualRoleProvideDispatcher
            ? peer.Provide<DualRoleProvideDispatcher>() : peer.Provide<ManualProvideDispatcher>(),
        "GenericHelper" => ProvideGeneric(peer, dispatcher),
        "Interface" => peer.Provide((IServiceDispatcher)dispatcher),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static RpcPeer ProvideGeneric<T>(RpcPeer peer, T dispatcher) where T : class, IServiceDispatcher =>
        peer.Provide(dispatcher);
}
