using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.ContextLifetime;

public sealed class RpcStreamingContextAccessorLifetimeTests
{
    [Theory]
    [InlineData("Stream")]
    [InlineData("Pipe")]
    [InlineData("Items")]
    public async Task Claimed_surfaces_remain_usable_after_context_completion(string kind)
    {
        var (manager, context, handle) = Create(kind);
        var surface = GetSurface(context, handle, kind);
        context.EnsureAllDeclaredInboundStreamsClaimed();
        Assert.Null(context.CompleteDispatch());

        await VerifyAndDispose(surface);

        manager.Stop();
        Assert.Throws<InvalidOperationException>(() => GetSurface(context, handle, kind));
    }

    [Theory]
    [InlineData("Stream")]
    [InlineData("Pipe")]
    [InlineData("Items")]
    public async Task Accessors_racing_completion_either_claim_a_usable_surface_or_reject_it(string kind)
    {
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var (manager, context, handle) = Create(kind);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var claim = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    return GetSurface(context, handle, kind);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            });
            var complete = Task.Run(async () =>
            {
                await start.Task;
                context.CompleteDispatch();
            });
            start.SetResult(true);
            await complete;
            var surface = await claim;
            if (surface is not null)
            {
                await VerifyAndDispose(surface);
            }

            manager.Stop();
        }
    }

    private static (RpcStreamManager Manager, RpcStreamingContext Context, RpcStreamHandle Handle) Create(string kind)
    {
        var serializer = new MessagePackRpcSerializer();
        var manager = new RpcStreamManager(serializer, static (_, _) => Task.CompletedTask, exceptionTransformer: null);
        var handle = new RpcStreamHandle(1, kind == "Items" ? RpcStreamKind.Items : RpcStreamKind.Binary);
        manager.RegisterInbound([handle], CancellationToken.None);
        Assert.True(manager.TryAcceptItem(1, MessageFramer.FrameToPayload(1, MessageType.StreamItem, new byte[] { 42 })));
        manager.CompleteInbound(1);
        return (manager, new RpcStreamingContext(manager, serializer, CancellationToken.None, [handle]), handle);
    }

    private static object GetSurface(RpcStreamingContext context, RpcStreamHandle handle, string kind) => kind switch
    {
        "Stream" => context.GetStream(handle),
        "Pipe" => context.GetPipe(handle),
        "Items" => context.GetAsyncEnumerable<int>(handle),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static async Task VerifyAndDispose(object surface)
    {
        switch (surface)
        {
            case Stream stream:
                await using (stream)
                {
                    var buffer = new byte[1];
                    Assert.Equal(1, await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.Equal(42, buffer[0]);
                    Assert.Equal(0, await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                }

                break;
            case Pipe pipe:
                var read = await pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(new byte[] { 42 }, read.Buffer.ToArray());
                pipe.Reader.AdvanceTo(read.Buffer.End);
                await pipe.Reader.CompleteAsync();
                break;
            case IAsyncEnumerable<int> items:
                await using (var enumerator = items.GetAsyncEnumerator())
                {
                    Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.Equal(42, enumerator.Current);
                    Assert.False(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                }

                break;
            default:
                throw new InvalidOperationException("Unexpected inbound surface.");
        }
    }
}
