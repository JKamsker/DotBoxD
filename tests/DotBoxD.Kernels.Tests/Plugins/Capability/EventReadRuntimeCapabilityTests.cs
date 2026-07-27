using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class EventReadRuntimeCapabilityTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task Runtime_rejects_gated_event_parameter_when_package_omits_event_read_metadata()
    {
        using var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var kernel = await server.InstallAsync(GatedEventPackage(includeMetadata: false));
        var adapter = new GatedRuntimeEventAdapter();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            () => kernel.ShouldHandleAsync(adapter, new GatedRuntimeEvent("target-1", 10)).AsTask());

        Assert.Equal(0, adapter.MaterializedValues);
        Assert.Contains(ex.Diagnostics, diagnostic =>
            diagnostic.Code == "DBXK044" &&
            diagnostic.Message.Contains("event.read.health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runtime_accepts_gated_event_parameter_when_event_read_metadata_is_granted()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("event.read.health", new { }, SandboxEffect.None)
            .WithFuel(10_000)
            .WithMaxHostCalls(10)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
        using var server = PluginServer.Create(defaultPolicy: policy);
        var kernel = await server.InstallAsync(GatedEventPackage(includeMetadata: true), policy);
        var adapter = new GatedRuntimeEventAdapter();

        var handled = await kernel.ShouldHandleAsync(adapter, new GatedRuntimeEvent("target-1", 10));

        Assert.True(handled);
        Assert.Equal(1, adapter.MaterializedValues);
    }

    [Theory]
    [InlineData("ShouldHandle", "Handle")]
    [InlineData("Handle", "ShouldHandle")]
    public void Event_capability_validation_requires_each_entrypoint_to_declare_gated_property(
        string declaredEntrypoint,
        string missingEntrypoint)
    {
        var plan = SplitEntrypointCapabilityPlan(declaredEntrypoint);

        var exception = Assert.Throws<SandboxValidationException>(
            () => PluginEventCapabilityValidator.Validate<GatedRuntimeEvent>(
                plan,
                new KernelEntrypoints("ShouldHandle", "Handle"),
                EventParameters()));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("DBXK044", diagnostic.Code);
        Assert.Contains(
            $"(missing: {missingEntrypoint}: event.read.health).",
            diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Event_capability_validation_keeps_derived_property_capability()
    {
        var plan = ModuleCapabilityPlan("event.read.derived-health");

        PluginEventCapabilityValidator.Validate<DerivedGatedRuntimeEvent>(
            plan,
            new KernelEntrypoints("ShouldHandle", "Handle"),
            [new Parameter("e_Health", SandboxType.I32)]);
    }

    [Fact]
    public void Event_capability_validation_keeps_inherited_property_capability()
    {
        var plan = ModuleCapabilityPlan("event.read.base-health");

        PluginEventCapabilityValidator.Validate<InheritedGatedRuntimeEvent>(
            plan,
            new KernelEntrypoints("ShouldHandle", "Handle"),
            [new Parameter("e_Health", SandboxType.I32)]);
    }

    private static PluginPackage GatedEventPackage(bool includeMetadata)
    {
        var metadata = new Dictionary<string, string>
        {
            ["pluginId"] = "gated-runtime",
            ["kernel"] = "GatedRuntimeKernel"
        };
        if (includeMetadata)
        {
            metadata["requiredCapabilities"] = "event.read.health";
        }

        return PluginPackage.Create(
            new PluginManifest(
                "gated-runtime",
                "IEventKernel<GatedRuntimeEvent>",
                ExecutionMode.Interpreted,
                [nameof(SandboxEffect.Cpu)],
                [],
                [new HookSubscriptionManifest(nameof(GatedRuntimeEvent), "GatedRuntimeKernel")])
            {
                RequiredCapabilities = includeMetadata ? ["event.read.health"] : []
            },
            new SandboxModule(
                "gated-runtime",
                SemVersion.One,
                SemVersion.One,
                [],
                [ShouldHandle(), Handle()],
                metadata));
    }

    private static SandboxFunction ShouldHandle()
        => new(
            "ShouldHandle",
            true,
            EventParameters(),
            SandboxType.Bool,
            [
                new ReturnStatement(
                    new BinaryExpression(
                        new VariableExpression("e_Health", Span),
                        ">",
                        new LiteralExpression(SandboxValue.FromInt32(0), Span),
                        Span),
                    Span)
            ]);

    private static SandboxFunction Handle()
        => new(
            "Handle",
            true,
            EventParameters(),
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

    private static ExecutionPlan SplitEntrypointCapabilityPlan(string declaredEntrypoint)
    {
        var binding = EventReadBinding();
        var bindings = new BindingRegistry([binding]);
        return new ExecutionPlan(
            "module",
            "plan",
            new ExecutionPlanSeal("seal"),
            "policy",
            bindings.ManifestHash,
            EmptyModule(),
            PluginAddendumTestPolicies.LongWall(),
            bindings,
            new ResourceLimits(),
            FunctionAnalysis(),
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["ShouldHandle"] = EntrypointBindings("ShouldHandle"),
                ["Handle"] = EntrypointBindings("Handle")
            });

        IReadOnlySet<string> EntrypointBindings(string entrypoint)
            => string.Equals(entrypoint, declaredEntrypoint, StringComparison.Ordinal)
                ? new HashSet<string>([binding.Id], StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
    }

    private static ExecutionPlan ModuleCapabilityPlan(string capability)
    {
        var bindings = new BindingRegistryBuilder().Build();
        return new ExecutionPlan(
            "module",
            "plan",
            new ExecutionPlanSeal("seal"),
            "policy",
            bindings.ManifestHash,
            EmptyModule([new CapabilityRequest(capability, "test")]),
            PluginAddendumTestPolicies.LongWall(),
            bindings,
            new ResourceLimits(),
            FunctionAnalysis(),
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["ShouldHandle"] = new HashSet<string>(StringComparer.Ordinal),
                ["Handle"] = new HashSet<string>(StringComparer.Ordinal)
            });
    }

    private static SandboxModule EmptyModule(IReadOnlyList<CapabilityRequest>? capabilityRequests = null)
        => new(
            "gated-runtime",
            SemVersion.One,
            SemVersion.One,
            capabilityRequests ?? [],
            [ShouldHandle(), Handle()],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pluginId"] = "gated-runtime",
                ["kernel"] = "GatedRuntimeKernel"
            });

    private static Dictionary<string, FunctionAnalysis> FunctionAnalysis()
        => new(StringComparer.Ordinal)
        {
            ["ShouldHandle"] = new(SandboxType.Bool, SandboxEffect.Cpu, true),
            ["Handle"] = new(SandboxType.Unit, SandboxEffect.Cpu, true)
        };

    private static BindingDescriptor EventReadBinding()
        => new(
            "test.event.read",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.HostStateRead | SandboxEffect.Audit,
            "event.read.health",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            static (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            static (_, _) => { });

    private static Parameter[] EventParameters() =>
    [
        new("e_TargetId", SandboxType.String),
        new("e_Health", SandboxType.I32)
    ];

    private sealed record GatedRuntimeEvent(
        string TargetId,
        [property: Capability("event.read.health")] int Health);

    private class BaseGatedRuntimeEvent
    {
        [Capability("event.read.base-health")]
        public int Health { get; init; }
    }

    private sealed class DerivedGatedRuntimeEvent : BaseGatedRuntimeEvent
    {
        [Capability("event.read.derived-health")]
        public new int Health { get; init; }
    }

    private sealed class InheritedGatedRuntimeEvent : BaseGatedRuntimeEvent;

    private sealed class GatedRuntimeEventAdapter : IPluginEventAdapter<GatedRuntimeEvent>
    {
        public int MaterializedValues { get; private set; }

        public string EventName => nameof(GatedRuntimeEvent);

        public IReadOnlyList<Parameter> Parameters => EventParameters();

        public IReadOnlyList<SandboxValue> ToSandboxValues(GatedRuntimeEvent e)
        {
            MaterializedValues++;
            return [SandboxValue.FromString(e.TargetId), SandboxValue.FromInt32(e.Health)];
        }
    }
}
