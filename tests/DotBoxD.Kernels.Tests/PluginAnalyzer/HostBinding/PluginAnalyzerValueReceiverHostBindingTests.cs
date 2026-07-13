using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class PluginAnalyzerValueReceiverHostBindingTests
{
    private const string Source = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace Sample;

        public sealed record TargetSnapshot(string Id, int Level)
        {
            [HostBinding(
                "host.target.meetsLevel",
                "target.read.level",
                SandboxEffect.Cpu | SandboxEffect.HostStateRead,
                IncludeReceiver = true)]
            public bool MeetsLevel(int minimum) => Level >= minimum;
        }

        public sealed record ValueReceiverEvent(TargetSnapshot Target, int Minimum);

        [Plugin("value-receiver")]
        public sealed partial class ValueReceiverKernel : IEventKernel<ValueReceiverEvent>
        {
            public bool ShouldHandle(ValueReceiverEvent e, HookContext ctx)
                => e.Target.MeetsLevel(e.Minimum);

            public void Handle(ValueReceiverEvent e, HookContext ctx)
                => ctx.Messages.Send(e.Target.Id, "matched");
        }
        """;

    [Fact]
    public async Task Explicit_value_receiver_binding_lowers_receiver_as_argument_zero_and_runs()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ValueReceiverPluginPackage");

        Assert.Contains("target.read.level", package.Manifest.RequiredCapabilities);
        using var server = PluginServer.Create(
            configureHost: AddBindings,
            defaultPolicy: ReadPolicy());
        var kernel = await server.InstallAsync(package);
        var adapter = new ValueReceiverEventAdapter();

        Assert.True(await kernel.ShouldHandleAsync(
            adapter,
            new ValueReceiverEvent(new TargetSnapshot("target-1", 10), 5)));
        Assert.False(await kernel.ShouldHandleAsync(
            adapter,
            new ValueReceiverEvent(new TargetSnapshot("target-2", 3), 5)));
    }

    private static void AddBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(new BindingDescriptor(
            "host.target.meetsLevel",
            SemVersion.One,
            [SandboxType.Record([SandboxType.String, SandboxType.I32]), SandboxType.I32],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "target.read.level",
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            static (context, arguments, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var target = (RecordValue)arguments[0];
                var targetId = ((StringValue)target.Fields[0]).Value;
                var level = ((I32Value)target.Fields[1]).Value;
                var minimum = ((I32Value)arguments[1]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.target.meetsLevel",
                    CapabilityId: "target.read.level",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: targetId,
                    Fields: context.BindingAuditFields("target", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(level >= minimum));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));
    }

    private static SandboxPolicy ReadPolicy() =>
        SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .Grant("target.read.level", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private sealed record TargetSnapshot(string Id, int Level);

    private sealed record ValueReceiverEvent(TargetSnapshot Target, int Minimum);

    private sealed class ValueReceiverEventAdapter : IPluginEventAdapter<ValueReceiverEvent>
    {
        public string EventName => "ValueReceiverEvent";

        public IReadOnlyList<Parameter> Parameters { get; } =
        [
            new(
                "e_Target",
                SandboxType.Record([SandboxType.String, SandboxType.I32])),
            new("e_Minimum", SandboxType.I32),
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ValueReceiverEvent value) =>
        [
            SandboxValue.FromRecord(
                [SandboxValue.FromString(value.Target.Id), SandboxValue.FromInt32(value.Target.Level)]),
            SandboxValue.FromInt32(value.Minimum),
        ];
    }
}
