using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

internal static class PluginEventAdapterValidationCacheTestFixture
{
    internal const string EventName = "SharedValidationEvent";
    internal const string ReadCapability = "event.read.cache-value";

    internal static readonly Parameter[] Parameters =
        [new("e_Value", SandboxType.I32)];

    internal static ValidationEnvironment Create(bool grantReadCapability)
    {
        var entrypoints = new KernelEntrypoints("ShouldHandle", "Handle");
        var functions = new[]
        {
            Function(entrypoints.ShouldHandle, SandboxType.Bool),
            Function(entrypoints.Handle, SandboxType.Unit)
        };
        var requests = grantReadCapability
            ? new[] { new CapabilityRequest(ReadCapability, "test event read") }
            : [];
        var module = new SandboxModule(
            "adapter-validation-cache",
            SemVersion.One,
            SemVersion.One,
            requests,
            functions,
            new Dictionary<string, string>(StringComparer.Ordinal));
        var bindings = new BindingRegistryBuilder().Build();
        var plan = new ExecutionPlan(
            "module-hash",
            "plan-hash",
            new ExecutionPlanSeal("plan-seal"),
            "policy-hash",
            bindings.ManifestHash,
            module,
            PluginAddendumTestPolicies.LongWall(),
            bindings,
            new ResourceLimits(),
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal));
        var manifest = new PluginManifest(
            "adapter-validation-cache",
            "IEventKernel<SharedValidationEvent>",
            ExecutionMode.Interpreted,
            [nameof(SandboxEffect.Cpu)],
            [],
            [new HookSubscriptionManifest(EventName, "ValidationKernel")]);

        return new ValidationEnvironment(manifest, plan, entrypoints);
    }

    private static SandboxFunction Function(string id, SandboxType returnType)
    {
        var span = new SourceSpan(1, 1);
        var value = returnType == SandboxType.Bool
            ? SandboxValue.FromBool(true)
            : SandboxValue.Unit;
        return new SandboxFunction(
            id,
            true,
            Parameters,
            returnType,
            [new ReturnStatement(new LiteralExpression(value, span), span)]);
    }

    internal sealed record ValidationEnvironment(
        PluginManifest Manifest,
        ExecutionPlan Plan,
        KernelEntrypoints Entrypoints)
    {
        internal IReadOnlyList<Parameter> Validate<TEvent>(
            PluginEventAdapterValidationCache cache,
            IPluginEventAdapter<TEvent> adapter)
            => cache.Validate(Manifest, Plan, Entrypoints, adapter);
    }
}

internal sealed class MutableValidationAdapter<TEvent> : IPluginEventValueWriter<TEvent>
{
    internal MutableValidationAdapter(IReadOnlyList<Parameter>? parameters = null)
    {
        Parameters = parameters ?? PluginEventAdapterValidationCacheTestFixture.Parameters;
    }

    public string EventName { get; set; } = PluginEventAdapterValidationCacheTestFixture.EventName;
    public IReadOnlyList<Parameter> Parameters { get; set; }
    public int EventValueCount => Parameters.Count + EventValueCountOffset;
    internal int EventValueCountOffset { get; set; }

    public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e)
        => throw new NotSupportedException("Shape validation does not materialize event values.");

    public SandboxValue ToSandboxValue(TEvent e, int index)
        => throw new NotSupportedException("Shape validation does not materialize event values.");

    public void CopySandboxValues(TEvent e, SandboxValue[] destination, int destinationIndex)
        => throw new NotSupportedException("Shape validation does not materialize event values.");
}

internal sealed record UngatedCacheEvent(int Value);

internal sealed record GatedCacheEvent(
    [property: Capability(PluginEventAdapterValidationCacheTestFixture.ReadCapability)] int Value);

internal sealed class DualEventValidationAdapter :
    IPluginEventValueWriter<UngatedCacheEvent>,
    IPluginEventValueWriter<GatedCacheEvent>
{
    public string EventName => PluginEventAdapterValidationCacheTestFixture.EventName;
    public IReadOnlyList<Parameter> Parameters => PluginEventAdapterValidationCacheTestFixture.Parameters;
    public int EventValueCount => Parameters.Count;

    public IReadOnlyList<SandboxValue> ToSandboxValues(UngatedCacheEvent e) => [SandboxValue.FromInt32(e.Value)];
    public IReadOnlyList<SandboxValue> ToSandboxValues(GatedCacheEvent e) => [SandboxValue.FromInt32(e.Value)];
    public SandboxValue ToSandboxValue(UngatedCacheEvent e, int index) => SandboxValue.FromInt32(e.Value);
    public SandboxValue ToSandboxValue(GatedCacheEvent e, int index) => SandboxValue.FromInt32(e.Value);

    public void CopySandboxValues(UngatedCacheEvent e, SandboxValue[] destination, int destinationIndex)
        => destination[destinationIndex] = SandboxValue.FromInt32(e.Value);

    public void CopySandboxValues(GatedCacheEvent e, SandboxValue[] destination, int destinationIndex)
        => destination[destinationIndex] = SandboxValue.FromInt32(e.Value);
}
