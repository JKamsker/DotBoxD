using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class EventQueryDispatcherLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disposed_subscriptions_release_collectible_event_types(bool indexed, bool compiled)
    {
        var host = new EventQueryHost();
        var reference = CreateAndRegister(host, indexed, compiled, dispose: true);

        await AssertCollected(reference);

        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_subscriptions_keep_event_types_alive_until_their_last_handle_is_disposed(bool indexed)
    {
        var host = new EventQueryHost();
        var reference = CreateAndRegister(host, indexed, compiled: true, dispose: false);
        Collect();
        Assert.True(reference.IsAlive, "An active subscription must strongly own its event type.");

        PublishAndDispose(host, reference);
        await AssertCollected(reference);

        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dropping_the_host_releases_a_collectible_event_type(bool indexed)
    {
        var reference = CreateAndRegister(new EventQueryHost(), indexed, compiled: true, dispose: true);

        await AssertCollected(reference);
    }

    [Fact]
    public async Task A_disposed_handle_keeps_its_diagnostics_valid_until_the_handle_is_released()
    {
        var host = new EventQueryHost();
        var (reference, handle) = CreateAndRetainHandle(host);
        Collect();
        Assert.True(reference.IsAlive);
        Assert.Equal(20, handle.EventsObserved);
        Assert.True(handle.IsCompiled);
        Assert.NotEmpty(handle.Describe());

        handle = null!;
        await AssertCollected(reference);

        GC.KeepAlive(host);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndRegister(EventQueryHost host, bool indexed, bool compiled, bool dispose)
    {
        var type = CreateEventType();
        Register(type, host, indexed, compiled, dispose);
        return new WeakReference(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, EventQuerySubscriptionHandle Handle) CreateAndRetainHandle(EventQueryHost host)
    {
        var type = CreateEventType();
        var handle = Register(type, host, indexed: true, compiled: true, dispose: true);
        return (new WeakReference(type), handle);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PublishAndDispose(EventQueryHost host, WeakReference reference)
    {
        var type = Assert.IsAssignableFrom<Type>(reference.Target);
        typeof(EventQueryDispatcherLifetimeTests).GetMethod(nameof(PublishAndDisposeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, [host]);
    }

    private static void PublishAndDisposeTyped<T>(EventQueryHost host) where T : EventState, new()
    {
        var input = new T();
        host.PublishAsync(input, Context()).GetAwaiter().GetResult();
        Assert.True(host.HasSubscriptions<T>());
        Assert.Equal(1, input.Calls);
        input.Handle!.Dispose();
        Assert.False(host.HasSubscriptions<T>());
    }

    private static EventQuerySubscriptionHandle Register(Type type, EventQueryHost host, bool indexed, bool compiled, bool dispose)
        => (EventQuerySubscriptionHandle)typeof(EventQueryDispatcherLifetimeTests)
            .GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, [host, indexed, compiled, dispose])!;

    private static EventQuerySubscriptionHandle RegisterTyped<T>(EventQueryHost host, bool indexed, bool compiled, bool dispose)
        where T : EventState, new()
    {
        EventQuerySubscriptionHandle handle = null!;
        var query = indexed ? host.Query<T>().Where(e => e.Value == 1) : host.Query<T>().Where(e => e.Value >= 1);
        handle = query.SubscribeAsync((input, _) =>
        {
            input.Calls++;
            input.Handle = handle;
            return ValueTask.CompletedTask;
        }).GetAwaiter().GetResult();
        var input = new T();
        for (var index = 0; index < (compiled ? 20 : 1); index++)
        {
            host.PublishAsync(input, Context()).GetAwaiter().GetResult();
        }

        Assert.Equal(compiled, handle.IsCompiled);
        Assert.Equal(indexed, handle.Plan.IsRoutable);
        Assert.Equal(compiled ? 20 : 1, input.Calls);
        if (dispose)
        {
            handle.Dispose();
            Assert.False(host.HasSubscriptions<T>());
        }

        return handle;
    }

    private static Type CreateEventType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"QueryDispatcher-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Events").DefineType("TransientEvent", TypeAttributes.Public, typeof(EventState));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        return builder.CreateType()!;
    }

    private static async Task AssertCollected(WeakReference reference)
    {
        var timer = Stopwatch.StartNew();
        while (reference.IsAlive && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Collect();
            if (reference.IsAlive)
            {
                await Task.Delay(10);
            }
        }

        Assert.False(reference.IsAlive, "An empty dispatcher must not retain an unused collectible event type.");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    public class EventState
    {
        public int Value => 1;
        public int Calls { get; set; }
        public EventQuerySubscriptionHandle? Handle { get; set; }
    }
}
