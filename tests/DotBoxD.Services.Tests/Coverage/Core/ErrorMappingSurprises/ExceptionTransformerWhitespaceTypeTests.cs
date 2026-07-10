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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandlerException_TransformerBlankMessage_FallsBackToInternalError(string? message)
    {
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => new RpcErrorInfo(message!, "APP_BLANK"),
        };

        var ex = await InvokeMissingPlayerAsync(serverOptions);

        Assert.Equal("Internal error.", ex.Message);
        Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
    }

    [Fact]
    public async Task HandlerException_TransformerWhitespaceType_FallsBackToInternalError()
    {
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => new RpcErrorInfo("safe", "   "),
        };

        var ex = await InvokeMissingPlayerAsync(serverOptions);

        Assert.Equal("safe", ex.Message);
        Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
    }

    private static async Task<RemoteServiceException> InvokeMissingPlayerAsync(RpcPeerOptions serverOptions)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var server = RpcPeer.Over(serverConnection, new MessagePackRpcSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        await using var client = RpcPeer.Over(
                clientConnection,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(8) })
            .Start();

        var game = client.GetGameService();

        return await Assert.ThrowsAsync<RemoteServiceException>(
            () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));
    }
}
