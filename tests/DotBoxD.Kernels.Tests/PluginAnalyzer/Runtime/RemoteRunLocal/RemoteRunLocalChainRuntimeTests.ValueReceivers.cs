using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using HostSupport = DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.PluginAnalyzerHostBindingTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string ValueReceiverRunSource = """
        using DotBoxD.Plugins.Runtime;
        using Ev = global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

        namespace ChainSample;

        public static class ValueReceiverRunUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>()
                    .Where(e => e.Player.MeetsLevel(5))
                    .Run((e, ctx) => ctx.Messages.Send(e.Zone, "matched"));
        }
        """;

    [Fact]
    public async Task Object_default_binding_executes_in_run_chain_from_referenced_sdk_metadata()
    {
        var package = LowerToPackage(ValueReceiverRunSource);
        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(
            messages,
            configureHost: AddValueReceiverBinding,
            defaultPolicy: HostSupport.ProbeReadPolicy());
        var kernel = await server.InstallAsync(package);
        server.Hooks.On<EncounterEvent>().Use(kernel);

        await server.Hooks.PublishAsync(EventWithPlayerLevel(10));
        await server.Hooks.PublishAsync(EventWithPlayerLevel(3));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("crypt", message.TargetId);
        Assert.Equal("matched", message.Message);
    }

    private static EncounterEvent EventWithPlayerLevel(int level)
        => new(
            SampleId,
            GamePhase.Battle,
            Boss: false,
            Distance: 3,
            Score: 10,
            Multiplier: 1,
            Zone: "crypt",
            MonsterIds: [1],
            Player: new PlayerInfo("player-1", level));

    private static void AddValueReceiverBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.player.MeetsLevel.i32",
            SemVersion.One,
            [SandboxType.Record([SandboxType.String, SandboxType.I32]), SandboxType.I32],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "probe.read.level",
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            static (context, arguments, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var player = (RecordValue)arguments[0];
                var name = ((StringValue)player.Fields[0]).Value;
                var level = ((I32Value)player.Fields[1]).Value;
                var minimum = ((I32Value)arguments[1]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.player.MeetsLevel.i32",
                    CapabilityId: "probe.read.level",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: name,
                    Fields: context.BindingAuditFields("player", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(level >= minimum));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));
}
