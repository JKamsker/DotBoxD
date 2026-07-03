using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServicePropertyBindingTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task AddBindingsFrom_registers_explicit_host_binding_properties()
    {
        using var host = SandboxHost.Create(
            builder => builder.AddBindingsFrom<IPropertyProbeWorld>(new PropertyProbeWorld()));
        var module = PropertyBindingModule();
        var policy = SandboxPolicyBuilder.Create()
            .Grant("probe.read.scalar", new { }, SandboxEffect.HostStateRead)
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(17, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Async_host_service_properties_observe_sandbox_wall_time_after_getter_invocation()
    {
        var world = new AsyncPropertyProbeWorld();
        using var host = SandboxHost.Create(
            builder => builder.AddBindingsFrom<IAsyncPropertyProbeWorld>(world));
        var module = PendingPropertyBindingModule();
        var policy = SandboxPolicyBuilder.Create()
            .Grant("probe.read.scalar", new { }, SandboxEffect.HostStateRead)
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .WithWallTime(TimeSpan.FromMilliseconds(20))
            .AllowRuntimeAsync()
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var executionTask = host.ExecuteAsync(plan, "main", SandboxValue.Unit).AsTask();

        try
        {
            var completed = await Task.WhenAny(
                executionTask,
                Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(executionTask, completed);
            var result = await executionTask;

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
            Assert.False(world.PendingScalarTask.IsCompleted);
        }
        finally
        {
            world.Release();
            await Task.WhenAny(executionTask, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    private interface IPropertyProbeWorld
    {
        [HostBinding("host.probe.scalar", "probe.read.scalar", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Scalar { get; }
    }

    private interface IAsyncPropertyProbeWorld
    {
        [HostBinding(
            "host.probe.pendingScalar",
            "probe.read.scalar",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            IsAsync = true)]
        Task<int> PendingScalar { get; }
    }

    private sealed class PropertyProbeWorld : IPropertyProbeWorld
    {
        public int Scalar => 17;
    }

    private sealed class AsyncPropertyProbeWorld : IAsyncPropertyProbeWorld
    {
        private readonly TaskCompletionSource<int> _pendingScalar = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> PendingScalar => _pendingScalar.Task;

        public Task PendingScalarTask => _pendingScalar.Task;

        public void Release()
            => _pendingScalar.TrySetResult(17);
    }

    private static SandboxModule PropertyBindingModule()
        => new(
            "property-binding-probe",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new CallExpression("host.probe.scalar", [], null, Span), Span)])
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static SandboxModule PendingPropertyBindingModule()
        => new(
            "pending-property-binding-probe",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new CallExpression("host.probe.pendingScalar", [], null, Span), Span)])
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));
}
