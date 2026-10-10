using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.GeneratedFixtures.Collectible;
using DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;
using DotBoxD.Services.Tests.Support;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Lifetime.Collectible;

public sealed class CollectibleRpcLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generated_rpc_and_runtime_type_deserialization_allow_context_unload(bool strictOptions)
        => await AssertCollected(await RunAndUnloadAsync(strictOptions));

    [Fact]
    public async Task Runtime_type_deserialization_does_not_retain_types_in_a_shared_serializer()
    {
        var serializer = new MessagePackRpcSerializer(
            MessagePackSerializerOptions.Standard.WithResolver(CollectibleReplayPayload.Resolver.Instance));
        await AssertCollected(DeserializeCollectible(serializer));
        GC.KeepAlive(serializer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> RunAndUnloadAsync(bool strictOptions)
    {
        var context = new AssemblyLoadContext("Collectible RPC fixture", isCollectible: true);
        var reference = new WeakReference(context);
        try
        {
            await RunAsync(context, strictOptions);
        }
        finally
        {
            context.Unload();
        }

        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunAsync(AssemblyLoadContext context, bool strictOptions)
    {
        var assembly = context.LoadFromAssemblyPath(typeof(CollectibleRpcFixture).Assembly.Location);
        var fixture = assembly.GetType(typeof(CollectibleRpcFixture).FullName!)!;
        var resolver = (IFormatterResolver)fixture.GetProperty("Resolver")!.GetValue(null)!;
        var serializer = strictOptions
            ? new MessagePackRpcSerializer(MessagePackRpcSerializer.CreateCollectibleOptions(resolver))
            : MessagePackRpcSerializer.CreateWithResolver(resolver);
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = RpcPeer.Over(clientConnection, serializer,
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        await using var server = RpcPeer.Over(serverConnection, serializer);
        await (Task)fixture.GetMethod("RoundTripAsync")!.Invoke(null, [client, server, serializer])!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DeserializeCollectible(MessagePackRpcSerializer serializer)
    {
        var type = CollectibleReplayPayload.CreateType(guarded: false);
        Assert.Null(serializer.Deserialize(new byte[] { MessagePackCode.Nil }, type));
        return new WeakReference(type);
    }

    private static async Task AssertCollected(WeakReference reference)
    {
        for (var i = 0; i < 100 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }

        Assert.False(reference.IsAlive, "DotBoxD must release unused collectible service and payload assemblies.");
    }
}
