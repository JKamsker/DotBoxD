using System.Buffers;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.ContextLifetime;

public sealed class RpcStreamingContextPeerLifetimeTests
{
    [Theory]
    [InlineData("Unary", true)]
    [InlineData("Unary", false)]
    [InlineData("Stream", true)]
    [InlineData("Stream", false)]
    [InlineData("Pipe", true)]
    [InlineData("Pipe", false)]
    [InlineData("Items", true)]
    [InlineData("Items", false)]
    public async Task Retained_dispatch_context_releases_completed_peer_resources(string mode, bool retain)
    {
        var fixture = await ExercisePeers(mode, retain);

        await fixture.AssertCollected();

        Assert.Equal(mode is "Stream" or "Items" ? 1 : 0, fixture.State.DisposeCalls);
        if (fixture.Context is not null)
        {
            using var rejected = new MemoryStream();
            Assert.Throws<InvalidOperationException>(() => fixture.Context.SetResponse(rejected));
        }

        GC.KeepAlive(fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<ContextLifetimeFixture> ExercisePeers(string mode, bool retain)
    {
        var state = new ContextLifetimeState();
        var serializer = new ContextLifetimeSerializer();
        var dispatcher = new ContextLifetimeDispatcher(mode, state);
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, serializer).Provide((IServiceDispatcher)dispatcher).Start();
        var client = RpcPeer.Over(clientConnection, serializer).Start();
        try
        {
            switch (mode)
            {
                case "Unary":
                    Assert.Equal(42, await client.InvokeAsync<int>(dispatcher.ServiceName, "Run").WaitAsync(TimeSpan.FromSeconds(10)));
                    break;
                case "Stream":
                    await using (var stream = await client.InvokeStreamAsync(dispatcher.ServiceName, "Run").WaitAsync(TimeSpan.FromSeconds(10)))
                    {
                        using var bytes = new MemoryStream();
                        await stream.CopyToAsync(bytes).WaitAsync(TimeSpan.FromSeconds(10));
                        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());
                    }

                    break;
                case "Pipe":
                    var pipe = await client.InvokePipeAsync(dispatcher.ServiceName, "Run").WaitAsync(TimeSpan.FromSeconds(10));
                    var collected = new List<byte>();
                    while (true)
                    {
                        var result = await pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                        collected.AddRange(result.Buffer.ToArray());
                        pipe.Reader.AdvanceTo(result.Buffer.End);
                        if (result.IsCompleted)
                        {
                            break;
                        }
                    }

                    await pipe.Reader.CompleteAsync();
                    Assert.Equal(new byte[] { 1, 2, 3 }, collected);
                    break;
                case "Items":
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var items = new List<int>();
                        await foreach (var item in client.InvokeAsyncEnumerable<int>(dispatcher.ServiceName, "Run").WithCancellation(timeout.Token))
                        {
                            items.Add(item);
                        }

                        Assert.Equal(new[] { 1, 2, 3 }, items);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }

        Assert.NotNull(dispatcher.Captured);
        return new ContextLifetimeFixture(retain ? dispatcher.Captured : null, new WeakReference(serializer), null, null, null, state);
    }
}
