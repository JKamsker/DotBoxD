using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerProxyFactoryDisposalTests
{
    [Fact]
    public async Task Get_WhenProxyFactoryDisposesPeer_ThrowsAndDoesNotReturnProxy()
    {
        var factoryCalls = 0;
        RegisterReentrantDisposalProxy(invoker =>
        {
            factoryCalls++;
            ((RpcPeer)invoker).DisposeAsync().AsTask().GetAwaiter().GetResult();
            return new ReentrantDisposalProxy();
        });

        await using var channel = new ScriptedConnection();
        await using var peer = RpcPeer.Over(channel, new MessagePackRpcSerializer());

        IReentrantDisposalService? publishedProxy = null;
        var exception = Record.Exception(() => publishedProxy = peer.Get<IReentrantDisposalService>());

        Assert.IsType<ObjectDisposedException>(exception);
        Assert.Null(publishedProxy);
        Assert.Equal(1, factoryCalls);
        Assert.Throws<ObjectDisposedException>(() => peer.Get<IReentrantDisposalService>());
    }

    private static void RegisterReentrantDisposalProxy(
        Func<IRpcInvoker, IReentrantDisposalService> proxyFactory) =>
        GeneratedServiceRegistry.Register(
            proxyFactory,
            _ => new ReentrantDisposalDispatcher(),
            new GeneratedService(
                typeof(IReentrantDisposalService),
                typeof(ReentrantDisposalProxy),
                typeof(ReentrantDisposalDispatcher),
                nameof(IReentrantDisposalService)));

    private interface IReentrantDisposalService;

    private sealed class ReentrantDisposalProxy : IReentrantDisposalService;

    private sealed class ReentrantDisposalDispatcher : IServiceDispatcher
    {
        public string ServiceName => nameof(IReentrantDisposalService);

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
