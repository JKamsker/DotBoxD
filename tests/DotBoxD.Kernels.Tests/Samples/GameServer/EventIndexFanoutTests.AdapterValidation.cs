extern alias GameServerAbstractions;

using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Indexing;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed partial class EventIndexFanoutTests
{
    [Fact]
    public async Task Index_registry_rejects_second_adapter_for_same_event_type()
    {
        var package = GeneratedAttackPackage();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            new RecordingMessageSink(),
            defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package, ChainPolicy());
        var subscription = Assert.Single(kernel.Manifest.Subscriptions);
        var registry = new EventIndexRegistry();

        Assert.True(registry.Register(
            server.Events.Resolve<AttackEvent>(),
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));

        var ex = Assert.Throws<SandboxValidationException>(() => registry.Register(
            AlternateAttackEventAdapter.Instance,
            kernel,
            subscription.IndexedPredicates,
            subscription.IndexCoversPredicate));
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK034");
    }

    private sealed class AlternateAttackEventAdapter : IPluginEventAdapter<AttackEvent>
    {
        public static AlternateAttackEventAdapter Instance { get; } = new();

        public string EventName => nameof(AttackEvent);

        public IReadOnlyList<Parameter> Parameters =>
        [
            new("e_" + nameof(AttackEvent.AttackerId), SandboxType.String),
            new("e_" + nameof(AttackEvent.TargetId), SandboxType.String),
            new("e_" + nameof(AttackEvent.Damage), SandboxType.I32),
            new("e_" + nameof(AttackEvent.AttackerLevel), SandboxType.I32),
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(AttackEvent e) =>
        [
            SandboxValue.FromString(e.AttackerId),
            SandboxValue.FromString(e.TargetId),
            SandboxValue.FromInt32(e.Damage),
            SandboxValue.FromInt32(e.AttackerLevel),
        ];
    }
}
