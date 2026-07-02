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
        var module = PropertyBindingModule("host.probe.scalar", "property-binding-probe");
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
    public async Task Task_like_property_binding_requires_runtime_async_when_IsAsync_is_omitted()
    {
        var world = new AsyncPropertyProbeWorld();
        using var host = SandboxHost.Create(
            builder => builder.AddBindingsFrom<IAsyncPropertyProbeWorld>(world));
        var module = PropertyBindingModule("host.probe.asyncScalar", "async-property-binding-probe");
        var policy = SandboxPolicyBuilder.Create()
            .Grant("probe.read.scalar", new { }, SandboxEffect.HostStateRead)
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .Build();

        ExecutionPlan plan;
        try
        {
            plan = await host.PrepareAsync(module, policy);
        }
        catch (SandboxValidationException ex)
        {
            Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
            Assert.Equal(0, world.GetterCalls);
            return;
        }

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(0, world.GetterCalls);
    }

    private interface IPropertyProbeWorld
    {
        [HostBinding("host.probe.scalar", "probe.read.scalar", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Scalar { get; }
    }

    private interface IAsyncPropertyProbeWorld
    {
        [HostBinding("host.probe.asyncScalar", "probe.read.scalar", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        Task<int> AsyncScalar { get; }
    }

    private sealed class PropertyProbeWorld : IPropertyProbeWorld
    {
        public int Scalar => 17;
    }

    private sealed class AsyncPropertyProbeWorld : IAsyncPropertyProbeWorld
    {
        public int GetterCalls { get; private set; }

        public Task<int> AsyncScalar
        {
            get
            {
                GetterCalls++;
                return Task.FromResult(17);
            }
        }
    }

    private static SandboxModule PropertyBindingModule(string bindingId, string moduleId)
        => new(
            moduleId,
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.I32,
                    [new ReturnStatement(new CallExpression(bindingId, [], null, Span), Span)])
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));
}
