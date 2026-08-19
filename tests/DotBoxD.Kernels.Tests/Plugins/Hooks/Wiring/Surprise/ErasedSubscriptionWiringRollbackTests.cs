using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Wiring;

public sealed class ErasedSubscriptionWiringRollbackTests
{
    [Fact]
    public async Task WireSubscription_rolls_back_new_pipeline_when_kernel_validation_fails()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        server.RegisterEventAdapter(FirstEventAdapter.Instance);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        Assert.True(server.Events.TryResolveErased(nameof(FirstEvent), out var erased));

        var wireException = Assert.Throws<DotBoxD.Kernels.Model.SandboxValidationException>(() => erased.WireSubscription(
            server.Subscriptions,
            kernel,
            new KernelWireTerminal(KernelWireKind.Plain, null, null, 0),
            default,
            null));

        Assert.Contains(wireException.Diagnostics, diagnostic => diagnostic.Code == "DBXK031");

        var registrationException = Record.Exception(
            () => server.RegisterEventAdapter(new ReplacementFirstEventAdapter()));

        Assert.Null(registrationException);
    }

    private sealed record FirstEvent(string TargetId);

    private sealed class FirstEventAdapter : IPluginEventAdapter<FirstEvent>
    {
        public static FirstEventAdapter Instance { get; } = new();

        public string EventName => nameof(FirstEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(FirstEvent e) => [SandboxValue.FromString(e.TargetId)];
    }

    private sealed class ReplacementFirstEventAdapter : IPluginEventAdapter<FirstEvent>
    {
        public string EventName => nameof(FirstEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_TargetId", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(FirstEvent e) => [SandboxValue.FromString(e.TargetId)];
    }
}
