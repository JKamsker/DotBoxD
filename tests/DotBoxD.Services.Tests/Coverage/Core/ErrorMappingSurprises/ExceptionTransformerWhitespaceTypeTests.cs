using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class ExceptionTransformerWhitespaceTypeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task HandlerException_TransformerWhitespaceType_FallsBackToInternalError()
    {
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => new RpcErrorInfo("safe", "   "),
        };

        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, new MessagePackRpcSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        var client = RpcPeer.Over(
                clientConnection,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(8) })
            .Start();

        try
        {
            var game = client.GetGameService();

            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            Assert.Equal("safe", ex.Message);
            Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }
}
