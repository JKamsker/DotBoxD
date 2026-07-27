namespace DotBoxD.Kernels.Benchmarks.Plugins.AdapterValidation;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Input;

internal static class EventAdapterValidationFixture
{
    internal const string Capability1 = "benchmark.event.read.value1";
    internal const string Capability2 = "benchmark.event.read.value2";
    internal const string Capability3 = "benchmark.event.read.value3";
    internal const string Capability4 = "benchmark.event.read.value4";
    internal const string Capability5 = "benchmark.event.read.value5";
    internal const string Capability6 = "benchmark.event.read.value6";
    internal const string Capability7 = "benchmark.event.read.value7";
    internal const string Capability8 = "benchmark.event.read.value8";

    internal static readonly Parameter[] OneParameter =
        [new("e_Value1", SandboxType.I32)];

    internal static readonly Parameter[] EightParameters =
    [
        new("e_Value1", SandboxType.I32),
        new("e_Value2", SandboxType.I32),
        new("e_Value3", SandboxType.I32),
        new("e_Value4", SandboxType.I32),
        new("e_Value5", SandboxType.I32),
        new("e_Value6", SandboxType.I32),
        new("e_Value7", SandboxType.I32),
        new("e_Value8", SandboxType.I32)
    ];

    internal static ValidationEnvironment Create<TEvent>(
        IReadOnlyList<Parameter> parameters,
        params string[] capabilities)
    {
        var eventName = typeof(TEvent).Name;
        var entrypoints = new KernelEntrypoints("ShouldHandle", "Handle");
        var functions = new[]
        {
            Function(entrypoints.ShouldHandle, parameters, SandboxType.Bool),
            Function(entrypoints.Handle, parameters, SandboxType.Unit)
        };
        var module = new SandboxModule(
            "event-adapter-validation",
            SemVersion.One,
            SemVersion.One,
            capabilities.Select(static id => new CapabilityRequest(id, "benchmark event read")).ToArray(),
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
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxHostCalls(10)
                .WithWallTime(TimeSpan.FromSeconds(10))
                .Build(),
            bindings,
            new ResourceLimits(),
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal));
        var manifest = new PluginManifest(
            "event-adapter-validation",
            $"IEventKernel<{eventName}>",
            ExecutionMode.Interpreted,
            [nameof(SandboxEffect.Cpu)],
            [],
            [new HookSubscriptionManifest(eventName, "ValidationProbe")]);

        return new ValidationEnvironment(manifest, plan, entrypoints, eventName);
    }

    private static SandboxFunction Function(
        string id,
        IReadOnlyList<Parameter> parameters,
        SandboxType returnType)
    {
        var span = new SourceSpan(1, 1);
        var value = returnType == SandboxType.Bool
            ? SandboxValue.FromBool(true)
            : SandboxValue.Unit;
        return new SandboxFunction(
            id,
            true,
            parameters,
            returnType,
            [new ReturnStatement(new LiteralExpression(value, span), span)]);
    }
}

internal sealed record ValidationEnvironment(
    PluginManifest Manifest,
    ExecutionPlan Plan,
    KernelEntrypoints Entrypoints,
    string EventName)
{
    internal int Validate<TEvent>(
        PluginEventAdapterValidationCache cache,
        ProbeEventAdapter<TEvent> adapter)
    {
        var parameters = cache.Validate(Manifest, Plan, Entrypoints, adapter);
        return ReferenceEquals(parameters, adapter.Parameters) ? parameters.Count + 1 : -1;
    }
}

internal sealed class ProbeEventAdapter<TEvent>(
    string eventName,
    IReadOnlyList<Parameter> parameters) : IPluginEventValueWriter<TEvent>
{
    public string EventName { get; } = eventName;
    public IReadOnlyList<Parameter> Parameters { get; } = parameters;
    public int EventValueCount => Parameters.Count;

    public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e)
        => throw new NotSupportedException("The validation probe does not materialize event values.");

    public SandboxValue ToSandboxValue(TEvent e, int index)
        => throw new NotSupportedException("The validation probe does not materialize event values.");

    public void CopySandboxValues(TEvent e, SandboxValue[] destination, int destinationIndex)
        => throw new NotSupportedException("The validation probe does not materialize event values.");
}

internal sealed record UngatedValidationEvent(int Value1);

internal sealed record OneCapabilityValidationEvent(
    [property: Capability(EventAdapterValidationFixture.Capability1)] int Value1);

internal sealed record EightCapabilityValidationEvent(
    [property: Capability(EventAdapterValidationFixture.Capability1)] int Value1,
    [property: Capability(EventAdapterValidationFixture.Capability2)] int Value2,
    [property: Capability(EventAdapterValidationFixture.Capability3)] int Value3,
    [property: Capability(EventAdapterValidationFixture.Capability4)] int Value4,
    [property: Capability(EventAdapterValidationFixture.Capability5)] int Value5,
    [property: Capability(EventAdapterValidationFixture.Capability6)] int Value6,
    [property: Capability(EventAdapterValidationFixture.Capability7)] int Value7,
    [property: Capability(EventAdapterValidationFixture.Capability8)] int Value8);
