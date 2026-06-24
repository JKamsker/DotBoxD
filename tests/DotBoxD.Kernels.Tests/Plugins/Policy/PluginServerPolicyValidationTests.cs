using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Policy;

public sealed class PluginServerPolicyValidationTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly SandboxType PlayerIdType = SandboxType.Scalar("PlayerId");

    [Fact]
    public async Task Install_preflight_uses_install_policy_for_declared_opaque_ids()
    {
        using var server = PluginServer.Create(defaultPolicy: SandboxPolicyBuilder.Create()
            .DeclareOpaqueIdType("PlayerId")
            .WithFuel(1_000)
            .Build());
        server.RegisterEventAdapter(PlayerEventAdapter.Instance);

        var kernel = await server.InstallAsync(PlayerPackage(), SandboxPolicyBuilder.Create()
            .DeclareOpaqueIdType("PlayerId")
            .WithFuel(1_000)
            .Build());

        Assert.False(kernel.IsRevoked);
        Assert.True(server.Uninstall("player-policy"));
    }

    private static PluginPackage PlayerPackage()
        => PluginPackage.Create(
            new PluginManifest(
                "player-policy",
                "IEventKernel<PlayerEvent>",
                ExecutionMode.Interpreted,
                [nameof(SandboxEffect.Cpu), nameof(SandboxEffect.Alloc)],
                [],
                [new HookSubscriptionManifest(nameof(PlayerEvent), "PlayerKernel")]),
            new SandboxModule(
                "player-policy",
                SemVersion.One,
                SemVersion.One,
                [],
                [ShouldHandle(), Handle()],
                new Dictionary<string, string>
                {
                    ["pluginId"] = "player-policy",
                    ["kernel"] = "PlayerKernel",
                }));

    private static SandboxFunction ShouldHandle()
        => new(
            "ShouldHandle",
            true,
            [new Parameter("playerId", PlayerIdType)],
            SandboxType.Bool,
            [new ReturnStatement(
                new BinaryExpression(
                    new VariableExpression("playerId", Span),
                    "==",
                    new LiteralExpression(SandboxValue.FromOpaqueId("PlayerId", "player-1"), Span),
                    Span),
                Span)]);

    private static SandboxFunction Handle()
        => new(
            "Handle",
            true,
            [new Parameter("playerId", PlayerIdType)],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

    private sealed record PlayerEvent(string PlayerId);

    private sealed class PlayerEventAdapter : IPluginEventAdapter<PlayerEvent>
    {
        public static PlayerEventAdapter Instance { get; } = new();

        public string EventName => nameof(PlayerEvent);
        public IReadOnlyList<Parameter> Parameters => [new("playerId", PlayerIdType)];
        public IReadOnlyList<SandboxValue> ToSandboxValues(PlayerEvent e) =>
            [SandboxValue.FromOpaqueId("PlayerId", e.PlayerId)];
    }
}
