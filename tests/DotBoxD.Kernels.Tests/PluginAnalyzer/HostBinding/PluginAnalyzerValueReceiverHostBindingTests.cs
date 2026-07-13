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

        [HostBindingObject(
            "host.target",
            "target.read.level",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        public sealed record TargetSnapshot(string Id, int Level)
        {
            public bool MeetsLevel(int minimum) => Level >= minimum;

            [HostBinding("target.read.label", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            public bool MeetsLevel(string label) => Id == label;

            [HostBinding(
                "host.target.hasExactId",
                "target.read.id",
                SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            public bool HasExactId(string id) => Id == id;

            [HostBindingIgnore]
            public string LocalLabel() => Id + ":" + Level;
        }

        public sealed record ValueReceiverEvent(TargetSnapshot Target, int Minimum, string Label);

        [Plugin("value-receiver")]
        public sealed partial class ValueReceiverKernel : IEventKernel<ValueReceiverEvent>
        {
            public bool ShouldHandle(ValueReceiverEvent e, HookContext ctx)
                => e.Target.MeetsLevel(e.Minimum) &&
                   e.Target.MeetsLevel(e.Label) &&
                   e.Target.HasExactId(e.Label);

            public void Handle(ValueReceiverEvent e, HookContext ctx)
                => ctx.Messages.Send(e.Target.Id, "matched");
        }
        """;

    private const string ExplicitSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace Sample;

        public sealed record TargetSnapshot(string Id, int Level)
        {
            [HostBinding(
                "host.target.explicitMeetsLevel",
                "target.read.level",
                SandboxEffect.Cpu | SandboxEffect.HostStateRead,
                IncludeReceiver = true)]
            public bool MeetsLevel(int minimum) => Level >= minimum;
        }

        public sealed record ExplicitReceiverEvent(TargetSnapshot Target, int Minimum, string Label);

        [Plugin("explicit-value-receiver")]
        public sealed partial class ExplicitReceiverKernel : IEventKernel<ExplicitReceiverEvent>
        {
            public bool ShouldHandle(ExplicitReceiverEvent e, HookContext ctx)
                => e.Target.MeetsLevel(e.Minimum);

            public void Handle(ExplicitReceiverEvent e, HookContext ctx)
                => ctx.Messages.Send(e.Target.Id, "matched");
        }
        """;

    [Fact]
    public async Task Object_binding_defaults_lower_receiver_as_argument_zero_and_run()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ValueReceiverPluginPackage");

        Assert.Contains("target.read.level", package.Manifest.RequiredCapabilities);
        Assert.Contains("target.read.label", package.Manifest.RequiredCapabilities);
        Assert.Contains("target.read.id", package.Manifest.RequiredCapabilities);
        using var server = PluginServer.Create(
            configureHost: AddBindings,
            defaultPolicy: ReadPolicy());
        var kernel = await server.InstallAsync(package);
        var adapter = new ValueReceiverEventAdapter();

        Assert.True(await kernel.ShouldHandleAsync(
            adapter,
            new ValueReceiverEvent(new TargetSnapshot("target-1", 10), 5, "target-1")));
        Assert.False(await kernel.ShouldHandleAsync(
            adapter,
            new ValueReceiverEvent(new TargetSnapshot("target-2", 3), 5, "target-2")));
    }

    [Fact]
    public async Task Explicit_binding_receiver_opt_in_still_runs_without_object_defaults()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ExplicitSource,
            "Sample.ExplicitReceiverPluginPackage");
        using var server = PluginServer.Create(
            configureHost: AddBindings,
            defaultPolicy: ReadPolicy());
        var kernel = await server.InstallAsync(package);

        Assert.True(await kernel.ShouldHandleAsync(
            new ValueReceiverEventAdapter("ExplicitReceiverEvent"),
            new ValueReceiverEvent(new TargetSnapshot("target-1", 10), 5, "target-1")));
    }

    private static void AddBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(BooleanTargetBinding(
            "host.target.MeetsLevel.i32",
            "target.read.level",
            SandboxType.I32,
            static (target, argument) => target.Level >= ((I32Value)argument).Value));
        builder.AddBinding(BooleanTargetBinding(
            "host.target.MeetsLevel.string",
            "target.read.label",
            SandboxType.String,
            static (target, argument) => target.Id == ((StringValue)argument).Value));
        builder.AddBinding(BooleanTargetBinding(
            "host.target.hasExactId",
            "target.read.id",
            SandboxType.String,
            static (target, argument) => target.Id == ((StringValue)argument).Value));
        builder.AddBinding(BooleanTargetBinding(
            "host.target.explicitMeetsLevel",
            "target.read.level",
            SandboxType.I32,
            static (target, argument) => target.Level >= ((I32Value)argument).Value));
    }

    private static BindingDescriptor BooleanTargetBinding(
        string bindingId,
        string capability,
        SandboxType argumentType,
        Func<TargetSnapshot, SandboxValue, bool> predicate)
        => new(
            bindingId,
            SemVersion.One,
            [SandboxType.Record([SandboxType.String, SandboxType.I32]), argumentType],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            capability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, arguments, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var target = (RecordValue)arguments[0];
                var targetId = ((StringValue)target.Fields[0]).Value;
                var level = ((I32Value)target.Fields[1]).Value;
                var argument = arguments[1];
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: bindingId,
                    CapabilityId: capability,
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: targetId,
                    Fields: context.BindingAuditFields("target", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(
                    predicate(new TargetSnapshot(targetId, level), argument)));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private static SandboxPolicy ReadPolicy() =>
        SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .Grant("target.read.level", new { }, SandboxEffect.HostStateRead)
            .Grant("target.read.label", new { }, SandboxEffect.HostStateRead)
            .Grant("target.read.id", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private sealed record TargetSnapshot(string Id, int Level);

    private sealed record ValueReceiverEvent(TargetSnapshot Target, int Minimum, string Label);

    private sealed class ValueReceiverEventAdapter : IPluginEventAdapter<ValueReceiverEvent>
    {
        public ValueReceiverEventAdapter(string eventName = "ValueReceiverEvent") => EventName = eventName;

        public string EventName { get; }

        public IReadOnlyList<Parameter> Parameters { get; } =
        [
            new(
                "e_Target",
                SandboxType.Record([SandboxType.String, SandboxType.I32])),
            new("e_Minimum", SandboxType.I32),
            new("e_Label", SandboxType.String),
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ValueReceiverEvent value) =>
        [
            SandboxValue.FromRecord(
                [SandboxValue.FromString(value.Target.Id), SandboxValue.FromInt32(value.Target.Level)]),
            SandboxValue.FromInt32(value.Minimum),
            SandboxValue.FromString(value.Label),
        ];
    }
}
