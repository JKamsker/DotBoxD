using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Wiring;

// A failing wire rolls back the pipeline it created, but only while nobody else has started using it: another
// caller that obtained the same pipeline mid-wire must not lose its registration.
public sealed class WirePipelineRollbackClaimTests
{
    [Fact]
    public async Task WireHook_rollback_keeps_pipeline_claimed_by_concurrent_caller()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var adapter = new ClaimingEventAdapter();
        server.RegisterEventAdapter(adapter);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Events.TryResolveErased(nameof(ProbeEvent), out var erased));
        var hits = 0;
        HookPipeline<ProbeEvent, HookContext>? claimed = null;
        adapter.OnParametersRead = () =>
            claimed = server.Hooks.On(adapter).InvokeHostHandler(_ => hits++);

        Assert.Throws<SandboxValidationException>(() => erased.WireHook(
            server.Hooks,
            kernel,
            new KernelWireTerminal(KernelWireKind.Plain, null, null, 0),
            default));
        await server.Hooks.PublishAsync(new ProbeEvent("target"));

        Assert.NotNull(claimed);
        Assert.Same(claimed, server.Hooks.On(adapter));
        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task WireSubscription_rollback_keeps_pipeline_claimed_by_concurrent_caller()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var adapter = new ClaimingEventAdapter();
        server.RegisterEventAdapter(adapter);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Events.TryResolveErased(nameof(ProbeEvent), out var erased));
        SubscriptionPipeline<ProbeEvent, HookContext>? claimed = null;
        adapter.OnParametersRead = () => claimed = server.Subscriptions.On(adapter);

        Assert.Throws<SandboxValidationException>(() => erased.WireSubscription(
            server.Subscriptions,
            kernel,
            new KernelWireTerminal(KernelWireKind.Plain, null, null, 0),
            default,
            null));

        Assert.NotNull(claimed);
        Assert.Same(claimed, server.Subscriptions.On(adapter));
    }

    private sealed record ProbeEvent(string TargetId);

    // The FireDamage kernel expects DamageEvent's parameters, so wiring it here always fails validation, after
    // ValidateFor has read Parameters: the callback runs inside the wire, between pipeline creation and rollback.
    private sealed class ClaimingEventAdapter : IPluginEventAdapter<ProbeEvent>
    {
        public Action? OnParametersRead { get; set; }

        public string EventName => nameof(ProbeEvent);

        public IReadOnlyList<Parameter> Parameters
        {
            get
            {
                var callback = OnParametersRead;
                OnParametersRead = null;
                callback?.Invoke();
                return [new("e_TargetId", SandboxType.String)];
            }
        }

        public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e) => [SandboxValue.FromString(e.TargetId)];
    }
}
