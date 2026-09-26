using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class MemberValueReaderCacheLifetimeTests
{
    [Theory]
    [InlineData(false, "Value")]
    [InlineData(true, "Value")]
    [InlineData(true, "Field")]
    [InlineData(true, "Child.Value")]
    public async Task Shared_readers_release_unused_collectible_event_types(bool readValues, string path)
    {
        var reader = new MemberValueReader();
        var reference = CreateAndRead(reader, readValues, path);

        await AssertCollected(reference);

        GC.KeepAlive(reader);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(20, true)]
    public async Task Active_base_event_subscription_does_not_retain_transient_derived_event_types(int publishCount, bool compiled)
    {
        var host = new EventQueryHost();
        var calls = 0;
        using var handle = await host.Query<EventState>()
            .Where(e => e.Value == "before")
            .SubscribeAsync((_, _) =>
            {
                calls++;
                return ValueTask.CompletedTask;
            });
        var reference = CreateAndPublish(host, publishCount);
        Assert.Equal(publishCount, calls);
        Assert.Equal(compiled, handle.IsCompiled);

        await AssertCollected(reference);
        await host.PublishAsync(new EventState(), NewContext());

        Assert.Equal(publishCount + 1, calls);
        Assert.Equal(publishCount + 1, handle.Dispatches);
        GC.KeepAlive(host);
    }

    private static async Task AssertCollected(WeakReference reference)
    {
        var timer = Stopwatch.StartNew();
        while (reference.IsAlive && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (reference.IsAlive)
            {
                await Task.Delay(10);
            }
        }

        Assert.False(reference.IsAlive, "The member-path cache must release unused collectible event types.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndRead(MemberValueReader reader, bool readValues, string path)
    {
        var (type, value) = CreateEvent();
        if (readValues)
        {
            Assert.Equal("before", reader.Read(value, path));
            value.Value = "after";
            value.Child.Value = "after";
            type.GetField("Field")!.SetValue(value, "after");
            Assert.Equal("after", reader.Read(value, path));
        }

        return new WeakReference(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndPublish(EventQueryHost host, int publishCount)
    {
        var (type, value) = CreateEvent();
        var context = NewContext();
        for (var i = 0; i < publishCount; i++)
        {
            var pending = host.PublishAsync(value, context);
            Assert.True(pending.IsCompleted);
            pending.GetAwaiter().GetResult();
        }

        return new WeakReference(type);
    }

    private static (Type Type, EventState Value) CreateEvent()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"QueryEvent-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Events")
            .DefineType("TransientEvent", TypeAttributes.Public, typeof(EventState));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        builder.DefineField("Field", typeof(string), FieldAttributes.Public);
        var type = builder.CreateType()!;
        var value = (EventState)Activator.CreateInstance(type)!;
        type.GetField("Field")!.SetValue(value, "before");
        return (type, value);
    }

    private static HookContext NewContext() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    public class EventState
    {
        public string Value { get; set; } = "before";
        public ChildState Child { get; } = new();
    }

    public sealed class ChildState
    {
        public string Value { get; set; } = "before";
    }
}
