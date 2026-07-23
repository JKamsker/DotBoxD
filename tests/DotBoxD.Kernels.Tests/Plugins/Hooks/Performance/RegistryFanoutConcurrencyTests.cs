using System.Collections;
using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

public sealed class RegistryFanoutConcurrencyTests
{
    private static readonly ProbeEventAdapter Adapter = new();
    private static readonly ProbeEvent Event = new(42);

    [Fact]
    public void Hook_publish_reads_miss_single_and_multi_snapshots_while_registry_gate_is_held()
    {
        using var server = PluginServer.Create();
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        FirstContext CreateFirst(HookContext context)
        {
            Interlocked.Increment(ref firstFactoryCalls);
            return new FirstContext(context);
        }

        SecondContext CreateSecond(HookContext context)
        {
            Interlocked.Increment(ref secondFactoryCalls);
            return new SecondContext(context);
        }

        lock (RegistryGate(server.Hooks))
        {
            RunOnWorker(
                () => server.Hooks.PublishAsync(Event).GetAwaiter().GetResult(),
                "hook miss lookup");

            server.Hooks.On<ProbeEvent, FirstContext>(Adapter, CreateFirst)
                .RunLocal(static (_, _) => { });
            RunOnWorker(
                () => server.Hooks.PublishAsync(Event).GetAwaiter().GetResult(),
                "hook single-pipeline lookup");

            Assert.Equal(1, Volatile.Read(ref firstFactoryCalls));
            Assert.Equal(0, Volatile.Read(ref secondFactoryCalls));

            server.Hooks.On<ProbeEvent, SecondContext>(Adapter, CreateSecond)
                .RunLocal(static (_, _) => { });
            RunOnWorker(
                () => server.Hooks.PublishAsync(Event).GetAwaiter().GetResult(),
                "hook multi-pipeline lookup");

            Assert.Equal(2, Volatile.Read(ref firstFactoryCalls));
            Assert.Equal(1, Volatile.Read(ref secondFactoryCalls));
        }
    }

    [Fact]
    public void Subscription_publish_reads_miss_single_and_multi_snapshots_while_registry_gate_is_held()
    {
        using var server = PluginServer.Create();
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        FirstContext CreateFirst(HookContext context)
        {
            Interlocked.Increment(ref firstFactoryCalls);
            return new FirstContext(context);
        }

        SecondContext CreateSecond(HookContext context)
        {
            Interlocked.Increment(ref secondFactoryCalls);
            return new SecondContext(context);
        }

        lock (RegistryGate(server.Subscriptions))
        {
            RunOnWorker(
                () => server.Subscriptions.Publish(Event),
                "subscription miss lookup");

            server.Subscriptions.On<ProbeEvent, FirstContext>(Adapter, CreateFirst)
                .RunLocal(static (_, _) => { });
            RunOnWorker(
                () => server.Subscriptions.Publish(Event),
                "subscription single-pipeline lookup");

            Assert.Equal(1, Volatile.Read(ref firstFactoryCalls));
            Assert.Equal(0, Volatile.Read(ref secondFactoryCalls));

            server.Subscriptions.On<ProbeEvent, SecondContext>(Adapter, CreateSecond)
                .RunLocal(static (_, _) => { });
            RunOnWorker(
                () => server.Subscriptions.Publish(Event),
                "subscription multi-pipeline lookup");

            Assert.Equal(2, Volatile.Read(ref firstFactoryCalls));
            Assert.Equal(1, Volatile.Read(ref secondFactoryCalls));
        }
    }

    [Fact]
    public async Task Hook_concurrent_context_registration_publishes_complete_final_fanout()
    {
        using var server = PluginServer.Create();
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        await server.Hooks.PublishAsync(Event);
        await RunConcurrentlyAsync(
            () => server.Hooks.On<ProbeEvent, FirstContext>(
                    Adapter,
                    context =>
                    {
                        Interlocked.Increment(ref firstFactoryCalls);
                        return new FirstContext(context);
                    })
                .RunLocal(static (_, _) => { }),
            () => server.Hooks.On<ProbeEvent, SecondContext>(
                    Adapter,
                    context =>
                    {
                        Interlocked.Increment(ref secondFactoryCalls);
                        return new SecondContext(context);
                    })
                .RunLocal(static (_, _) => { }));

        await server.Hooks.PublishAsync(Event);

        Assert.Equal(1, Volatile.Read(ref firstFactoryCalls));
        Assert.Equal(1, Volatile.Read(ref secondFactoryCalls));
    }

    [Fact]
    public async Task Subscription_concurrent_context_registration_publishes_complete_final_fanout()
    {
        using var server = PluginServer.Create();
        var firstFactoryCalls = 0;
        var secondFactoryCalls = 0;

        server.Subscriptions.Publish(Event);
        await RunConcurrentlyAsync(
            () => server.Subscriptions.On<ProbeEvent, FirstContext>(
                    Adapter,
                    context =>
                    {
                        Interlocked.Increment(ref firstFactoryCalls);
                        return new FirstContext(context);
                    })
                .RunLocal(static (_, _) => { }),
            () => server.Subscriptions.On<ProbeEvent, SecondContext>(
                    Adapter,
                    context =>
                    {
                        Interlocked.Increment(ref secondFactoryCalls);
                        return new SecondContext(context);
                    })
                .RunLocal(static (_, _) => { }));

        server.Subscriptions.Publish(Event);

        Assert.Equal(1, Volatile.Read(ref firstFactoryCalls));
        Assert.Equal(1, Volatile.Read(ref secondFactoryCalls));
    }

    [Fact]
    public void Hook_registration_replaces_the_published_dictionary_without_mutating_old_snapshots()
    {
        using var server = PluginServer.Create();
        var empty = PublishedFanout(server.Hooks);

        server.Hooks.On<ProbeEvent>(Adapter);
        var single = PublishedFanout(server.Hooks);
        var singleEntry = single[typeof(ProbeEvent)];

        Assert.NotSame(empty, single);
        Assert.Empty(empty);
        Assert.Single(single);
        Assert.NotNull(singleEntry);

        server.Hooks.On<ProbeEvent, FirstContext>(Adapter, static context => new FirstContext(context));
        var multiple = PublishedFanout(server.Hooks);

        Assert.NotSame(single, multiple);
        Assert.Equal(singleEntry, single[typeof(ProbeEvent)]);
        Assert.NotEqual(singleEntry, multiple[typeof(ProbeEvent)]);
    }

    [Fact]
    public void Subscription_registration_replaces_the_published_dictionary_without_mutating_old_snapshots()
    {
        using var server = PluginServer.Create();
        var empty = PublishedFanout(server.Subscriptions);

        server.Subscriptions.On<ProbeEvent>(Adapter);
        var single = PublishedFanout(server.Subscriptions);
        var singleEntry = single[typeof(ProbeEvent)];

        Assert.NotSame(empty, single);
        Assert.Empty(empty);
        Assert.Single(single);
        Assert.NotNull(singleEntry);

        server.Subscriptions.On<ProbeEvent, FirstContext>(Adapter, static context => new FirstContext(context));
        var multiple = PublishedFanout(server.Subscriptions);

        Assert.NotSame(single, multiple);
        Assert.Equal(singleEntry, single[typeof(ProbeEvent)]);
        Assert.NotEqual(singleEntry, multiple[typeof(ProbeEvent)]);
    }

    private static object RegistryGate(object registry)
        => registry.GetType().GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(registry)
            ?? throw new InvalidOperationException($"{registry.GetType().Name} does not expose its mutation gate.");

    private static IDictionary PublishedFanout(object registry)
        => (IDictionary?)(registry.GetType()
                .GetField("_pipelineFanout", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(registry))
            ?? throw new InvalidOperationException($"{registry.GetType().Name} does not expose its fanout snapshot.");

    private static void RunOnWorker(Action action, string operation)
    {
        var task = Task.Run(action);
        Assert.True(
            SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(10)),
            $"The {operation} waited for the registry mutation gate instead of reading published state.");
        task.GetAwaiter().GetResult();
    }

    private static async Task RunConcurrentlyAsync(Action first, Action second)
    {
        using var start = new ManualResetEventSlim();
        var firstTask = Task.Run(() =>
        {
            start.Wait();
            first();
        });
        var secondTask = Task.Run(() =>
        {
            start.Wait();
            second();
        });

        start.Set();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
    {
        public string EventName => "test.registry-fanout-concurrency";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e) => [];
    }

    private readonly record struct ProbeEvent(int Value);
    private sealed record FirstContext(HookContext Raw);
    private sealed record SecondContext(HookContext Raw);
}
