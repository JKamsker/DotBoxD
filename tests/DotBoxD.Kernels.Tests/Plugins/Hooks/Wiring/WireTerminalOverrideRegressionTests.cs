using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Wiring;

public sealed class WireTerminalOverrideRegressionTests
{
    [Fact]
    public async Task WireHook_rejects_invalid_terminal_override_before_registering_pipeline()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        var wireException = Record.Exception(() => server.WireHook(
            kernel,
            new WireOptions
            {
                ClassifyOverride = terminal => terminal with { Kind = (KernelWireKind)999 },
            }));

        Assert.NotNull(wireException);
        var registrationException = Record.Exception(
            () => server.RegisterEventAdapter(new AlternateDamageEventAdapter()));

        Assert.Null(registrationException);
    }

    [Fact]
    public async Task WireHook_rolls_back_new_pipeline_when_terminal_validation_fails()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.RegisterEventAdapter(new MismatchedDamageEventAdapter());

        var wireException = Record.Exception(() => server.WireHook(
            kernel,
            new WireOptions
            {
                LocalPush = (_, _, _) => ValueTask.CompletedTask,
                ClassifyOverride = terminal => terminal with
                {
                    Kind = KernelWireKind.Projecting,
                    CallbackSubscriptionId = "local-damage",
                },
            }));

        Assert.NotNull(wireException);
        var registrationException = Record.Exception(
            () => server.RegisterEventAdapter(DamageEventAdapter.Instance));

        Assert.Null(registrationException);
    }

    private sealed class AlternateDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }

    private sealed class MismatchedDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("alt_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }
}
