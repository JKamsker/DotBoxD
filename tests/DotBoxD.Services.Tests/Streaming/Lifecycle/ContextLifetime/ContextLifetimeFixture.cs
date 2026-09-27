using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.ContextLifetime;

internal sealed record ContextLifetimeFixture(
    RpcStreamingContext? Context,
    WeakReference Serializer,
    WeakReference? Sender,
    WeakReference? TokenSource,
    WeakReference? DeclaredHandles,
    ContextLifetimeState State)
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ContextLifetimeFixture Create(string operation, bool retain)
    {
        var state = new ContextLifetimeState();
        var sender = new ContextLifetimeSender(state);
        var serializer = new ContextLifetimeSerializer(operation == "SerializationFailure");
        var streams = new RpcStreamManager(serializer, sender.SendAsync, exceptionTransformer: null);
        using var cancellation = new CancellationTokenSource();
        RpcStreamHandle[]? declared = operation == "CompleteInbound" ? [new RpcStreamHandle(41, RpcStreamKind.Binary)] : null;
        streams.RegisterInbound(declared, CancellationToken.None);
        var context = new RpcStreamingContext(streams, serializer, cancellation.Token, declared);
        if (declared is not null)
        {
            context.GetStream(declared[0]).Dispose();
        }

        if (operation == "Abandon")
        {
            context.AbandonResponseAsync().AsTask().GetAwaiter().GetResult();
        }
        else if (operation != "Active")
        {
            var mode = operation is "Complete" or "CompleteInbound" or "UnknownService" ? "Unary" : operation;
            var dispatcher = new ContextLifetimeDispatcher(mode, state, cancellation);
            var dispatchers = new Dictionary<string, IServiceDispatcher>(StringComparer.Ordinal);
            if (operation != "UnknownService")
            {
                dispatchers.Add(dispatcher.ServiceName, dispatcher);
            }

            var builder = new RpcDispatchResponseBuilder(serializer, dispatchers);
            var request = new RpcRequest { MessageId = 1, ServiceName = dispatcher.ServiceName, MethodName = "Run" };
            if (operation is "SerializationFailure" or "CanceledDispatch")
            {
                var error = Record.Exception(() => builder.BuildAsync(request, 1, ReadOnlyMemory<byte>.Empty,
                    new InstanceRegistry(), context, cancellation.Token).AsTask().GetAwaiter().GetResult());
                if (operation == "CanceledDispatch")
                {
                    Assert.IsAssignableFrom<OperationCanceledException>(error);
                }
                else
                {
                    Assert.IsType<InvalidOperationException>(error);
                }
            }
            else
            {
                using var result = builder.BuildAsync(request, 1, ReadOnlyMemory<byte>.Empty,
                    new InstanceRegistry(), context, cancellation.Token).AsTask().GetAwaiter().GetResult();
                if (operation == "AbandonResponse")
                {
                    Assert.NotNull(result.Stream);
                    context.AbandonResponseAsync().AsTask().GetAwaiter().GetResult();
                }
                else if (operation == "PendingResponse")
                {
                    Assert.NotNull(result.Stream);
                }
                else
                {
                    Assert.Null(result.Stream);
                }
            }

            if (operation != "UnknownService")
            {
                Assert.Same(context, dispatcher.Captured);
            }
        }

        if (operation is not "Active" and not "PendingResponse")
        {
            streams.Stop();
        }

        return new ContextLifetimeFixture(retain ? context : null, new WeakReference(serializer), new WeakReference(sender),
            new WeakReference(cancellation), declared is null ? null : new WeakReference(declared), state);
    }

    public async Task AssertCollected()
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!Serializer.IsAlive && Sender?.IsAlive is not true && TokenSource?.IsAlive is not true &&
                DeclaredHandles?.IsAlive is not true && State.Source?.IsAlive is not true)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(Serializer.IsAlive, "A completed context must release its serializer.");
        Assert.False(Sender?.IsAlive is true, "A completed context must release its connection sender.");
        Assert.False(TokenSource?.IsAlive is true, "A completed context must release its request token.");
        Assert.False(DeclaredHandles?.IsAlive is true, "A completed context must release its declared handles.");
        Assert.False(State.Source?.IsAlive is true, "A completed context must release its response source after ownership ends.");
    }

    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
