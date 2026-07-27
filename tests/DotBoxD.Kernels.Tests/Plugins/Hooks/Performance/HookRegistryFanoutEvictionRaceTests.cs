using System.Reflection;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Performance;

public sealed class HookRegistryFanoutEvictionRaceTests
{
    private static readonly DamageEvent Event = new("fire", 120, "player-1");

    [Fact]
    public async Task Kernel_eviction_racing_context_registration_publishes_both_mutations()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var removedPipeline = server.Hooks.On<DamageEvent>(DamageEventAdapter.Instance);
        var fallbackPipeline = server.Hooks.On<DamageEvent, FallbackContext>(
            DamageEventAdapter.Instance,
            static context => new FallbackContext(context));
        AddSandboxResult(removedPipeline, kernel, priority: 100);
        AddDirectResult(fallbackPipeline, priority: 0, value: 1);

        Assert.Equal(0, Fire(server));

        using var removerStarted = new ManualResetEventSlim();
        Thread? removerThread = null;
        Task removal;
        lock (PipelineGate(removedPipeline))
        {
            lock (PipelineGate(fallbackPipeline))
            {
                removal = Task.Run(() =>
                {
                    removerThread = Thread.CurrentThread;
                    removerStarted.Set();
                    server.Hooks.RemoveKernel(kernel);
                });

                Assert.True(removerStarted.Wait(TimeSpan.FromSeconds(10)));
                Assert.NotNull(removerThread);
                Assert.True(SpinWait.SpinUntil(
                    () => (removerThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(10)));
                Assert.False(removal.IsCompleted);

                var addedPipeline = server.Hooks.On<DamageEvent, AddedContext>(
                    DamageEventAdapter.Instance,
                    static context => new AddedContext(context));
                AddDirectResult(addedPipeline, priority: 50, value: 2);
            }
        }

        await removal.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(removedPipeline.ResultRegistrations().Registrations);
        Assert.Equal(2, Fire(server));
    }

    private static int? Fire(PluginServer server)
        => server.Hooks.FireAsync<DamageEvent, RaceResult>(Event).GetAwaiter().GetResult()?.Value;

    private static void AddSandboxResult<TContext>(
        HookPipeline<DamageEvent, TContext> pipeline,
        InstalledKernel kernel,
        int priority)
        => ResultSlot(pipeline).AddSandbox(
            kernel,
            priority,
            static _ => new RaceResult(Success: true, Value: 0));

    private static void AddDirectResult<TContext>(
        HookPipeline<DamageEvent, TContext> pipeline,
        int priority,
        int value)
        => ResultSlot(pipeline).AddDirect(
            priority,
            (_, _, _) => ValueTask.FromResult<IHookResult?>(new RaceResult(Success: true, value)));

    private static ResultHookSlot<DamageEvent, TContext> ResultSlot<TContext>(
        HookPipeline<DamageEvent, TContext> pipeline)
        => (ResultHookSlot<DamageEvent, TContext>)(PipelineFields<TContext>.ResultSlot.GetValue(pipeline) ??
            throw new InvalidOperationException("The result-hook slot was not initialized."));

    private static object PipelineGate<TContext>(HookPipeline<DamageEvent, TContext> pipeline)
        => PipelineFields<TContext>.Gate.GetValue(pipeline) ??
            throw new InvalidOperationException("The hook-pipeline gate was not initialized.");

    private static class PipelineFields<TContext>
    {
        internal static readonly FieldInfo Gate = RequiredField("_gate");
        internal static readonly FieldInfo ResultSlot = RequiredField("_resultHooks");

        private static FieldInfo RequiredField(string name)
            => typeof(HookPipeline<DamageEvent, TContext>).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException($"The hook-pipeline field '{name}' could not be found.");
    }

    private readonly record struct RaceResult(bool Success, int Value) : IHookResult;
    private sealed record FallbackContext(HookContext Raw);
    private sealed record AddedContext(HookContext Raw);
}
