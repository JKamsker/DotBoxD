using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using static DotBoxD.Kernels.Tests.Plugins.Hooks.Performance.ResultHookFanoutTestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

public sealed class RegistryFanoutCacheRaceTests
{
    [Fact]
    public void Removed_wire_pipeline_cannot_be_republished_by_old_aggregate_builder()
    {
        using var setup = BuildAggregateWhileRemovingWirePipeline();

        CollectGarbage();

        Assert.False(setup.RemovedPipeline.IsAlive);
        Assert.False(setup.RemovedRegistration.IsAlive);
        Assert.Equal(1, Fire(setup.Server.Hooks)?.Value);
        GC.KeepAlive(setup.Server);
    }

    [Fact]
    public void Kernel_removal_detaches_cache_owner_from_delayed_aggregate_builder()
    {
        using var setup = PublishOldAggregateAfterKernelRemoval();

        CollectGarbage();

        Assert.False(setup.RemovedRegistration.IsAlive);
        Assert.Equal(2, Fire(setup.Server.Hooks)?.Value);
        GC.KeepAlive(setup.Server);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetentionSetup BuildAggregateWhileRemovingWirePipeline()
    {
        var server = PluginServer.Create();
        try
        {
            var surviving = server.Hooks.On<ProbeEvent, Context1>(
                Adapter,
                static raw => new Context1(raw));
            var removed = server.Hooks.OnForWire(Adapter, out var created);
            Assert.True(created);

            AddResult(surviving, priority: 100, value: 1);
            AddResult(removed, priority: 200, value: 2);
            var removedRegistration = Assert.Single(removed.ResultRegistrations().Registrations);

            // Make the first source lookup enter its monitor after FireAsync has captured the old fanout.
            ClearResultRegistrationSnapshot(surviving);
            var sourceGate = PipelineGate(surviving);
            using var builderStarted = new ManualResetEventSlim();
            Thread? builderThread = null;
            Task<ProbeResult?> oldBuilder;
            lock (sourceGate)
            {
                oldBuilder = Task.Run(() =>
                {
                    builderThread = Thread.CurrentThread;
                    builderStarted.Set();
                    return Fire(server.Hooks);
                });
                Assert.True(builderStarted.Wait(TimeSpan.FromSeconds(10)));
                Assert.NotNull(builderThread);
                Assert.True(SpinWait.SpinUntil(
                    () => (builderThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(10)));
                Assert.False(oldBuilder.IsCompleted);

                server.Hooks.RemoveWirePipeline(Adapter, removed);
            }

            // The in-flight dispatch owns a stable old fanout; later dispatches must use the replacement.
            Assert.Equal(2, oldBuilder.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()?.Value);
            Assert.Equal(1, Fire(server.Hooks)?.Value);

            return new RetentionSetup(
                server,
                new WeakReference(removed),
                new WeakReference(removedRegistration));
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static KernelRetentionSetup PublishOldAggregateAfterKernelRemoval()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        try
        {
            var kernel = server.InstallAsync(FireDamagePluginPackage.Create())
                .AsTask()
                .GetAwaiter()
                .GetResult();
            var removedResult = server.Hooks.On<ProbeEvent>(Adapter);
            var survivingResult = server.Hooks.On<ProbeEvent, Context1>(
                Adapter,
                static raw => new Context1(raw));
            AddSandboxHandler(removedResult, kernel, priority: 200);
            AddResult(survivingResult, priority: 100, value: 2);

            var removedRegistration = Assert.Single(removedResult.ResultRegistrations().Registrations);
            var oldFanout = MultipleFanout(server.Hooks);
            Assert.Null(oldFanout.ReadResultRegistrationCache());

            using var aggregateCreated = new ManualResetEventSlim();
            using var publishAggregate = new ManualResetEventSlim();
            var oldBuilder = Task.Run(() =>
            {
                var created = ResultHookRegistrationFanoutSnapshot<ProbeEvent>.Create(oldFanout);
                aggregateCreated.Set();
                publishAggregate.Wait();
                Assert.Null(oldFanout.CompareExchangeResultRegistrationCache(created, comparand: null));
                return created;
            });

            Assert.True(aggregateCreated.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(server.Uninstall("fire-damage"));
            publishAggregate.Set();

            var stale = oldBuilder.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.Same(removedRegistration, stale.Registrations[0]);
            Assert.Same(stale, oldFanout.ReadResultRegistrationCache());

            // Membership did not change, but eviction must have published a distinct cache owner.
            var replacement = MultipleFanout(server.Hooks);
            Assert.Equal(oldFanout.Count, replacement.Count);
            Assert.Null(replacement.ReadResultRegistrationCache());
            Assert.Equal(2, Fire(server.Hooks)?.Value);
            var current = Assert.IsType<ResultHookRegistrationFanoutSnapshot<ProbeEvent>>(
                replacement.ReadResultRegistrationCache());
            Assert.Single(current.Registrations);
            Assert.DoesNotContain(removedRegistration, current.Registrations);

            return new KernelRetentionSetup(server, new WeakReference(removedRegistration));
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    private static CachedPipelineFanout MultipleFanout(HookRegistry hooks)
    {
        var lookup = (Dictionary<Type, (object? Single, CachedPipelineFanout Multiple)>)(
            PipelineFanoutField.GetValue(hooks) ??
            throw new InvalidOperationException("The hook-registry fanout lookup was not initialized."));
        return lookup[typeof(ProbeEvent)].Multiple;
    }

    private static void CollectGarbage()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static ProbeResult? Fire(HookRegistry hooks)
        => hooks.FireAsync<ProbeEvent, ProbeResult>(Event).GetAwaiter().GetResult();

    private sealed record RetentionSetup(
        PluginServer Server,
        WeakReference RemovedPipeline,
        WeakReference RemovedRegistration) : IDisposable
    {
        public void Dispose() => Server.Dispose();
    }

    private sealed record KernelRetentionSetup(
        PluginServer Server,
        WeakReference RemovedRegistration) : IDisposable
    {
        public void Dispose() => Server.Dispose();
    }

    private static readonly FieldInfo PipelineFanoutField =
        typeof(HookRegistry).GetField("_pipelineFanout", BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("The hook-registry fanout lookup field could not be found.");
}
