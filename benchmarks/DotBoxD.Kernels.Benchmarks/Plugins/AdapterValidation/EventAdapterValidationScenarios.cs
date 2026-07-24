namespace DotBoxD.Kernels.Benchmarks.Plugins.AdapterValidation;

using DotBoxD.Kernels;
using DotBoxD.Plugins.Runtime.Input;

internal sealed class EventAdapterValidationScenario(
    string name,
    int iterations,
    Action prepare,
    Func<int> invoke,
    long expectedChecksum)
{
    internal const int WarmupIterations = 20_000;

    internal string Name { get; } = name;
    internal int Iterations { get; } = iterations;
    internal long ExpectedChecksum { get; } = expectedChecksum;

    internal void Prepare()
    {
        prepare();
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = invoke();
        }
    }

    internal int Invoke() => invoke();
}

internal static class EventAdapterValidationScenarios
{
    private const int WarmIterations = 1_000_000;
    private const int ColdIterations = 100_000;

    internal static IEnumerable<EventAdapterValidationScenario> Create()
    {
        yield return DirectShapeValidation();
        yield return Warm<OneCapabilityValidationEvent>(
            "Warm gated, 1 capability",
            EventAdapterValidationFixture.OneParameter,
            EventAdapterValidationFixture.Capability1);
        yield return Warm<EightCapabilityValidationEvent>(
            "Warm gated, 8 capabilities",
            EventAdapterValidationFixture.EightParameters,
            EventAdapterValidationFixture.Capability1,
            EventAdapterValidationFixture.Capability2,
            EventAdapterValidationFixture.Capability3,
            EventAdapterValidationFixture.Capability4,
            EventAdapterValidationFixture.Capability5,
            EventAdapterValidationFixture.Capability6,
            EventAdapterValidationFixture.Capability7,
            EventAdapterValidationFixture.Capability8);
        yield return AlternatingWarmAdapters();
        yield return Warm<UngatedValidationEvent>(
            "Warm ungated target",
            EventAdapterValidationFixture.OneParameter);
        yield return ConstructedMisses();
        yield return PreallocatedMisses();
    }

    private static EventAdapterValidationScenario DirectShapeValidation()
    {
        var parameters = EventAdapterValidationFixture.OneParameter;
        var environment = EventAdapterValidationFixture.Create<UngatedValidationEvent>(parameters);
        var adapter = new ProbeEventAdapter<UngatedValidationEvent>(environment.EventName, parameters);

        return new EventAdapterValidationScenario(
            "Direct shape-validator control",
            WarmIterations,
            () => ValidateShape(adapter, parameters),
            () => ValidateShape(adapter, parameters),
            Expected(WarmIterations, parameters.Length));
    }

    private static int ValidateShape<TEvent>(
        ProbeEventAdapter<TEvent> adapter,
        IReadOnlyList<Parameter> parameters)
    {
        PluginEventAdapterShapeValidator.Validate(adapter, adapter.EventName, parameters);
        return parameters.Count + 1;
    }

    private static EventAdapterValidationScenario Warm<TEvent>(
        string name,
        IReadOnlyList<Parameter> parameters,
        params string[] capabilities)
    {
        var environment = EventAdapterValidationFixture.Create<TEvent>(parameters, capabilities);
        var cache = new PluginEventAdapterValidationCache();
        var adapter = new ProbeEventAdapter<TEvent>(environment.EventName, parameters);
        return new EventAdapterValidationScenario(
            name,
            WarmIterations,
            () => _ = environment.Validate(cache, adapter),
            () => environment.Validate(cache, adapter),
            Expected(WarmIterations, parameters.Count));
    }

    private static EventAdapterValidationScenario AlternatingWarmAdapters()
    {
        var parameters = EventAdapterValidationFixture.OneParameter;
        var environment = EventAdapterValidationFixture.Create<OneCapabilityValidationEvent>(
            parameters,
            EventAdapterValidationFixture.Capability1);
        var cache = new PluginEventAdapterValidationCache();
        var first = new ProbeEventAdapter<OneCapabilityValidationEvent>(environment.EventName, parameters);
        var second = new ProbeEventAdapter<OneCapabilityValidationEvent>(environment.EventName, parameters.ToArray());
        var next = 0;

        return new EventAdapterValidationScenario(
            "Alternating warmed equal adapters",
            WarmIterations,
            () =>
            {
                _ = environment.Validate(cache, first);
                _ = environment.Validate(cache, second);
            },
            () => environment.Validate(cache, (next++ & 1) == 0 ? first : second),
            Expected(WarmIterations, parameters.Length));
    }

    private static EventAdapterValidationScenario ConstructedMisses()
    {
        var parameters = EventAdapterValidationFixture.OneParameter;
        var environment = EventAdapterValidationFixture.Create<OneCapabilityValidationEvent>(
            parameters,
            EventAdapterValidationFixture.Capability1);
        var cache = new PluginEventAdapterValidationCache();

        return new EventAdapterValidationScenario(
            "Steady cold constructed misses",
            ColdIterations,
            () => PrimeCapabilityMetadata(environment, parameters),
            () => environment.Validate(
                cache,
                new ProbeEventAdapter<OneCapabilityValidationEvent>(environment.EventName, parameters)),
            Expected(ColdIterations, parameters.Length));
    }

    private static EventAdapterValidationScenario PreallocatedMisses()
    {
        var parameters = EventAdapterValidationFixture.OneParameter;
        var environment = EventAdapterValidationFixture.Create<OneCapabilityValidationEvent>(
            parameters,
            EventAdapterValidationFixture.Capability1);
        var cache = new PluginEventAdapterValidationCache();
        var adapters = Enumerable.Range(
                0,
                EventAdapterValidationScenario.WarmupIterations + ColdIterations)
            .Select(_ => new ProbeEventAdapter<OneCapabilityValidationEvent>(environment.EventName, parameters))
            .ToArray();
        var index = 0;

        return new EventAdapterValidationScenario(
            "Steady cold preallocated misses",
            ColdIterations,
            () => PrimeCapabilityMetadata(environment, parameters),
            () => environment.Validate(cache, adapters[index++]),
            Expected(ColdIterations, parameters.Length));
    }

    private static void PrimeCapabilityMetadata(
        ValidationEnvironment environment,
        IReadOnlyList<Parameter> parameters)
    {
        var cache = new PluginEventAdapterValidationCache();
        var adapter = new ProbeEventAdapter<OneCapabilityValidationEvent>(environment.EventName, parameters);
        _ = environment.Validate(cache, adapter);
    }

    private static long Expected(int iterations, int parameterCount)
        => (long)iterations * (parameterCount + 1);
}
