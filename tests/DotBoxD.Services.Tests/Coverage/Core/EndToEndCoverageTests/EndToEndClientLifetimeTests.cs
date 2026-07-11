using DotBoxD.Services.Tests.Support;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.EndToEndCoverageTestSupport;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class EndToEndClientLifetimeTests
{
    [Fact]
    public async Task InvokeAfterClientDisposed_ThrowsObjectDisposed()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            Assert.Equal("1.0.0-test", (await game.GetServerStatusAsync().WaitAsync(EndToEndTimeout)).Version);

            await client.DisposeAsync();

            // The cached proxy now points at a disposed peer: its next call must fail fast. RpcPeer
            // guards the start path with an ObjectDisposedException (see EnsureStarted) rather than
            // attempting a doomed send, so the disposed object is the surfaced fault.
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(
                () => game.GetServerStatusAsync().WaitAsync(EndToEndTimeout));
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

}
