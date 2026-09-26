using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public sealed class ConstructorReplayGuardLifetimeTests
{
    [Theory]
    [InlineData("None", false)]
    [InlineData("None", true)]
    [InlineData("Reflection", false)]
    [InlineData("Reflection", true)]
    [InlineData("Compiled", false)]
    [InlineData("Compiled", true)]
    public async Task Serialization_releases_unused_collectible_payload_types(string mode, bool exactDeclaration)
    {
        var serializer = Serializer();
        var reference = CreateAndSerialize(serializer, mode, exactDeclaration);

        await AssertCollected(reference);

        GC.KeepAlive(serializer);
    }

    [Fact]
    public async Task Unserialized_payload_types_are_collectible()
        => await AssertCollected(CreateWithoutSerialization());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_payloads_preserve_the_shared_guard_until_released(bool compiled)
    {
        var serializer = Serializer();
        var lease = CreateAndRetain(serializer, compiled);

        Collect();
        VerifyLiveGuard(serializer, lease);
        lease.Owner.Value = null;
        await AssertCollected(lease.Type);
        await AssertCollected(lease.Guard);

        GC.KeepAlive(serializer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWithoutSerialization()
        => new(CollectibleReplayPayload.CreateType(guarded: true));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndSerialize(MessagePackRpcSerializer serializer, string mode, bool exactDeclaration)
    {
        var type = CollectibleReplayPayload.CreateType(guarded: mode != "None");
        var value = Activator.CreateInstance(type, mode == "None" ? [] : [42])!;
        Serialize(serializer, value, exactDeclaration, mode == "Compiled");
        if (mode != "None")
        {
            Assert.Equal(mode == "Compiled", ConstructorReplayTestSupport.GetValidator(type) is not null);
        }

        return new WeakReference(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Lease CreateAndRetain(MessagePackRpcSerializer serializer, bool compiled)
    {
        var type = CollectibleReplayPayload.CreateType(guarded: true);
        var value = Activator.CreateInstance(type, [42])!;
        Serialize(serializer, value, exactDeclaration: false, compiled);
        Assert.Equal(compiled, ConstructorReplayTestSupport.GetValidator(type) is not null);
        return new Lease(new WeakReference(type), new WeakReference(ConstructorReplayTestSupport.GetGuard(type)), new StrongBox<object?>(value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyLiveGuard(MessagePackRpcSerializer serializer, Lease lease)
    {
        var value = Assert.IsAssignableFrom<CollectibleReplayPayload.Value>(lease.Owner.Value);
        Assert.True(lease.Type.IsAlive);
        Assert.True(lease.Guard.IsAlive);
        Assert.Equal(42, value.Id);
        Assert.Same(lease.Guard.Target, ConstructorReplayTestSupport.GetGuard(value.GetType()));
        Serialize(serializer, value, exactDeclaration: true, compiled: false);
        Assert.Same(lease.Guard.Target, ConstructorReplayTestSupport.GetGuard(value.GetType()));
        GC.KeepAlive(value);
    }

    private static void Serialize(MessagePackRpcSerializer serializer, object value, bool exactDeclaration, bool compiled)
    {
        var iterations = compiled ? ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold + 1 : 1;
        if (exactDeclaration)
        {
            typeof(ConstructorReplayGuardLifetimeTests).GetMethod(nameof(SerializeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(value.GetType()).Invoke(null, [serializer, value, iterations]);
        }
        else
        {
            SerializeTyped<object>(serializer, value, iterations);
        }
    }

    private static void SerializeTyped<T>(MessagePackRpcSerializer serializer, object value, int iterations)
    {
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < iterations; i++)
        {
            serializer.Serialize(writer, (T)value);
            Assert.Equal(1, writer.WrittenCount);
            Assert.Equal(MessagePackCode.Nil, writer.WrittenSpan[0]);
            writer.Clear();
        }
    }

    private static MessagePackRpcSerializer Serializer()
        => new(MessagePackSerializerOptions.Standard.WithResolver(CollectibleReplayPayload.Resolver.Instance));

    private static async Task AssertCollected(WeakReference reference)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!reference.IsAlive)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(reference.IsAlive, "Serialization must not retain an otherwise unused collectible payload type or its guard.");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Lease(WeakReference Type, WeakReference Guard, StrongBox<object?> Owner);
}
