using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceBindingCancellationTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly TimeSpan GuardTimeout = TimeSpan.FromSeconds(1);
    private static readonly string BindingId =
        $"host.{typeof(IAsyncProbeWorld).Namespace}.{nameof(IAsyncProbeWorld)}.{nameof(IAsyncProbeWorld.GetAsync)}";

    [Fact]
    public async Task Async_service_binding_return_task_observes_sandbox_wall_time_after_invocation()
    {
        var world = new AsyncProbeWorld();
        using var host = SandboxHost.Create(
            builder => builder.AddBindingsFrom<IAsyncProbeWorld>(world));
        var plan = await host.PrepareAsync(AsyncBindingModule(), AsyncProbePolicy());

        var executeTask = host.ExecuteAsync(plan, "main", SandboxValue.Unit).AsTask();

        try
        {
            var completed = await Task.WhenAny(executeTask, Task.Delay(GuardTimeout));

            Assert.Same(executeTask, completed);
            Assert.Equal(1, world.Calls);
            Assert.False(world.Completion.Task.IsCompleted);

            var result = await executeTask;
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
        }
        finally
        {
            world.Completion.TrySetResult(0);
        }
    }

    private interface IAsyncProbeWorld
    {
        [HostCapability("probe.read.async", HostBindingEffect.HostStateRead)]
        Task<int> GetAsync();
    }

    private sealed class AsyncProbeWorld : IAsyncProbeWorld
    {
        private int _calls;

        public TaskCompletionSource<int> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public Task<int> GetAsync()
        {
            Interlocked.Increment(ref _calls);
            return Completion.Task;
        }
    }

    private static SandboxPolicy AsyncProbePolicy()
        => SandboxPolicyBuilder.Create()
            .Grant("probe.read.async", new { }, SandboxEffect.HostStateRead)
            .AllowRuntimeAsync()
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .WithWallTime(TimeSpan.FromMilliseconds(30))
            .Build();

    private static SandboxModule AsyncBindingModule()
        => new(
            "async-host-service-binding-probe",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new CallExpression(BindingId, [], null, Span), Span)])
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));
}
