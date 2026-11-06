using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

/// <summary>
/// A response stream outlives its dispatch slot. A remote caller that requests streams and never
/// reads or cancels them must not be able to make the serving peer hold unbounded pump state.
/// </summary>
public sealed class StreamingResponseStreamLimitTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ResponseStreamsBeyondLimit_AreRejected_UntilAStreamEnds()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { MaxResponseStreamsPerPeer = 2 })
            .Provide(new InfiniteNumbersDispatcher())
            .Start();
        await using var client = RpcPeer.Over(clientConnection, serializer).Start();

        var first = await OpenAsync(client);
        var second = await OpenAsync(client);

        var rejected = await Assert.ThrowsAsync<RemoteServiceException>(() => OpenAsync(client));
        Assert.Equal(RpcErrorTypes.ResponseStreamLimit, rejected.RemoteExceptionType);

        await first.DisposeAsync();
        var replacement = await OpenWhenSlotFreesAsync(client).WaitAsync(TestTimeout);

        await second.DisposeAsync();
        await replacement.DisposeAsync();
    }

    private static async Task<IAsyncEnumerator<int>> OpenAsync(RpcPeer client)
    {
        var enumerator = client.InvokeAsyncEnumerable<int>("Infinite", "Numbers").GetAsyncEnumerator();
        try
        {
            Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
            return enumerator;
        }
        catch
        {
            await enumerator.DisposeAsync();
            throw;
        }
    }

    private static async Task<IAsyncEnumerator<int>> OpenWhenSlotFreesAsync(RpcPeer client)
    {
        while (true)
        {
            try
            {
                return await OpenAsync(client);
            }
            catch (RemoteServiceException ex) when (ex.RemoteExceptionType == RpcErrorTypes.ResponseStreamLimit)
            {
                await Task.Delay(10);
            }
        }
    }
}
