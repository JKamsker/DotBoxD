using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using Shared;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal static class EndToEndCoverageTestSupport
{
    internal static readonly TimeSpan EndToEndTimeout = TimeSpan.FromSeconds(15);

    internal static MessagePackRpcSerializer NewSerializer() => new();

    internal static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = TimeSpan.FromSeconds(10) };

    internal static (RpcPeer Server, RpcPeer Client, IGameService Game) StartInMemoryPair(
        Action<RpcPeer> configureServer,
        int writeChunkSize = 0)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair(writeChunkSize);
        var server = RpcPeer.Over(serverConnection, NewSerializer());
        configureServer(server);
        server.Start();

        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        return (server, client, client.GetGameService());
    }

    internal static async Task InvokeUntilFailsAsync(IGameService game)
    {
        for (var i = 0; i < 50; i++)
        {
            await game.GetServerStatusAsync().ConfigureAwait(false);
            await Task.Yield();
        }
    }
}
