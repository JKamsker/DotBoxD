using System.Runtime.CompilerServices;
using DotBoxD.Plugins;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using static DotBoxD.Kernels.Tests.Plugins.Hooks.Performance.ResultHookFanoutTestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

public sealed class ResultHookFanoutMutationTests
{
    [Fact]
    public void Per_pipeline_registration_snapshot_changes_only_with_entry_snapshot()
    {
        using var server = PluginServer.Create();
        var pipeline = server.Hooks.On<ProbeEvent>(Adapter);

        var empty = pipeline.ResultRegistrations();
        Assert.Same(empty, pipeline.ResultRegistrations());

        AddPipeline(pipeline, includeHandler: true);
        var populated = pipeline.ResultRegistrations();

        Assert.NotSame(empty, populated);
        Assert.Same(populated, pipeline.ResultRegistrations());
        Assert.Single(populated.Registrations);
    }

    [Fact]
    public void Handler_added_after_cached_dispatch_is_observed_and_recached()
    {
        using var server = PluginServer.Create();
        var first = server.Hooks.On<ProbeEvent>(Adapter);
        var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
        AddPipeline(first, includeHandler: true);
        Assert.Null(Fire(server.Hooks));

        var calls = new InvocationCounter();
        AddHandler(second, priority: 10, (_, _, _) =>
        {
            calls.Value++;
            return ValueTask.FromResult<IHookResult?>(null);
        });

        Assert.Null(Fire(server.Hooks));
        Assert.Equal(1, calls.Value);
        Assert.Null(Fire(server.Hooks));
        Assert.Equal(2, calls.Value);
    }

    [Fact]
    public void Context_pipeline_add_and_remove_replace_warm_fanout()
    {
        using var server = PluginServer.Create();
        var first = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
        var second = server.Hooks.On<ProbeEvent, Context2>(Adapter, static raw => new Context2(raw));
        AddResult(first, priority: 10, value: 1);
        AddResult(second, priority: 20, value: 2);
        Assert.Equal(2, Fire(server.Hooks)?.Value);

        var added = server.Hooks.OnForWire(Adapter, out var created);
        Assert.True(created);
        AddResult(added, priority: 30, value: 3);
        Assert.Equal(3, Fire(server.Hooks)?.Value);

        server.Hooks.RemoveWirePipeline(Adapter, added);
        Assert.Equal(2, Fire(server.Hooks)?.Value);
    }

    [Fact]
    public async Task Kernel_handler_removal_replaces_slot_and_aggregate_snapshots()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var first = server.Hooks.On<ProbeEvent>(Adapter);
        var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
        AddSandboxHandler(first, kernel, priority: 0);
        AddResult(second, priority: 100, value: 2);
        Assert.Equal(2, Fire(server.Hooks)?.Value);
        var beforeRemoval = first.ResultRegistrations();
        Assert.Single(beforeRemoval.Registrations);

        Assert.True(server.Uninstall("fire-damage"));

        var afterRemoval = first.ResultRegistrations();
        Assert.NotSame(beforeRemoval, afterRemoval);
        Assert.Empty(afterRemoval.Registrations);
        Assert.Equal(2, Fire(server.Hooks)?.Value);
    }

    [Fact]
    public void Uninstall_releases_warm_registration_caches_without_another_dispatch()
    {
        using var setup = WarmAndUninstallKernelRegistration();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(setup.Registration.IsAlive);
        Assert.False(setup.Kernel.IsAlive);
        GC.KeepAlive(setup.Server);

        Assert.Equal(2, Fire(setup.Server.Hooks)?.Value);
    }

    [Fact]
    public async Task Cache_builder_waiting_on_pipeline_gate_cannot_republish_removed_entries()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var pipeline = server.Hooks.On<ProbeEvent>(Adapter);
        AddSandboxHandler(pipeline, kernel, priority: 0);
        _ = pipeline.ResultRegistrations();
        ClearResultRegistrationSnapshot(pipeline);

        var gate = PipelineGate(pipeline);
        using var builderStarted = new ManualResetEventSlim();
        Thread? builderThread = null;
        Task<ResultHookRegistrationSnapshot<ProbeEvent>> builder;
        lock (gate)
        {
            builder = Task.Run(() =>
            {
                builderThread = Thread.CurrentThread;
                builderStarted.Set();
                return pipeline.ResultRegistrations();
            });
            Assert.True(builderStarted.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotNull(builderThread);
            Assert.True(SpinWait.SpinUntil(
                () => (builderThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(10)));

            ((IKernelHandlerPipeline)pipeline).RemoveKernel(kernel);
        }

        var published = await builder.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(published.Registrations);
        Assert.Same(published, pipeline.ResultRegistrations());
    }

    [Fact]
    public async Task Concurrent_kernel_removals_cannot_regress_published_entry_snapshot()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var firstPackage = FireDamagePluginPackage.Create();
        var secondPackage = WithPluginId(firstPackage, "fire-damage-result-fanout-second");
        var firstKernel = await server.InstallAsync(firstPackage);
        var secondKernel = await server.InstallAsync(secondPackage);
        var pipeline = server.Hooks.On<ProbeEvent>(Adapter);
        AddSandboxHandler(pipeline, firstKernel, priority: 0);
        AddSandboxHandler(pipeline, secondKernel, priority: 1);
        Assert.Equal(2, pipeline.ResultRegistrations().Registrations.Length);

        await Task.WhenAll(
            Task.Run(() => ((IKernelHandlerPipeline)pipeline).RemoveKernel(firstKernel)),
            Task.Run(() => ((IKernelHandlerPipeline)pipeline).RemoveKernel(secondKernel)));

        var published = pipeline.ResultRegistrations();
        Assert.Empty(published.Registrations);
        Assert.Same(published, pipeline.ResultRegistrations());
    }

    [Fact]
    public void Equal_priority_cache_order_uses_global_registration_order()
    {
        using var server = PluginServer.Create();
        var first = server.Hooks.On<ProbeEvent>(Adapter);
        var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
        AddResult(second, priority: 5, value: 2);
        AddResult(first, priority: 5, value: 1);

        Assert.Equal(2, Fire(server.Hooks)?.Value);
        Assert.Equal(2, Fire(server.Hooks)?.Value);
    }

    [Fact]
    public async Task Concurrent_handler_add_does_not_change_inflight_registration_snapshot()
    {
        using var server = PluginServer.Create();
        var first = server.Hooks.On<ProbeEvent>(Adapter);
        var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IHookResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        AddHandler(first, priority: 100, (_, _, _) =>
        {
            entered.TrySetResult(true);
            return new ValueTask<IHookResult?>(release.Task);
        });
        AddResult(second, priority: 0, value: 1);

        var inflight = server.Hooks.FireAsync<ProbeEvent, ProbeResult>(Event).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AddResult(second, priority: 200, value: 2);
        release.SetResult(null);

        Assert.Equal(1, (await inflight)?.Value);
        Assert.Equal(2, Fire(server.Hooks)?.Value);
    }

    private static ProbeResult? Fire(HookRegistry hooks)
        => hooks.FireAsync<ProbeEvent, ProbeResult>(Event).GetAwaiter().GetResult();

    private static PluginPackage WithPluginId(PluginPackage package, string pluginId)
    {
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["pluginId"] = pluginId,
        };
        return package with
        {
            Manifest = package.Manifest with { PluginId = pluginId },
            Module = package.Module with { Id = pluginId, Metadata = metadata },
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetentionSetup WarmAndUninstallKernelRegistration()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = server.InstallAsync(FireDamagePluginPackage.Create()).AsTask().GetAwaiter().GetResult();
        var first = server.Hooks.On<ProbeEvent>(Adapter);
        var second = server.Hooks.On<ProbeEvent, Context1>(Adapter, static raw => new Context1(raw));
        AddSandboxHandler(first, kernel, priority: 0);
        AddResult(second, priority: 100, value: 2);
        if (Fire(server.Hooks)?.Value != 2)
        {
            throw new InvalidOperationException("The retained fallback hook did not win before uninstall.");
        }

        var registration = Assert.Single(first.ResultRegistrations().Registrations);
        var setup = new RetentionSetup(
            server,
            new WeakReference(kernel),
            new WeakReference(registration));
        if (!server.Uninstall("fire-damage"))
        {
            setup.Dispose();
            throw new InvalidOperationException("The kernel registration was not uninstalled.");
        }

        return setup;
    }

    private sealed record RetentionSetup(
        PluginServer Server,
        WeakReference Kernel,
        WeakReference Registration) : IDisposable
    {
        public void Dispose() => Server.Dispose();
    }
}
