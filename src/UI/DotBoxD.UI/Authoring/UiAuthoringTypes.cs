using System.Collections.Immutable;

namespace DotBoxD.UI.Authoring;

/// <summary>Worker-side composition. Only the resulting UiPackage crosses the process boundary.</summary>
public interface IUiComponent
{
    UiElement Render(UiBuilder builder);
}

public readonly record struct UiState<T>(int Id);
public readonly record struct UiBoundKernel<T>(int Id);
public sealed record UiKernelDefinition<TInput, TOutput>(string ModuleJson, string Entrypoint);
public readonly record struct UiUnit;
public sealed record UiElement(int Id);

/// <summary>Typed sugar over the public property source IDs.</summary>
public readonly record struct UiBinding<T>(UiValue? Literal = null, int StateSlotId = 0, int KernelId = 0, bool TwoWay = false)
{
    public static implicit operator UiBinding<T>(UiState<T> state) => new(StateSlotId: state.Id);
    public static implicit operator UiBinding<T>(UiBoundKernel<T> kernel) => new(KernelId: kernel.Id);
    public static UiBinding<T> Input(UiState<T> state) => new(StateSlotId: state.Id, TwoWay: true);
    public UiProperty Property(UiPropertyId id) => new(id, Literal, StateSlotId, KernelId, TwoWay);
}

public static class UiLiteral
{
    public static UiBinding<string> Text(string value) => new(UiValue.FromString(value));
    public static UiBinding<int> Integer(int value) => new(UiValue.FromInt32(value));
    public static UiBinding<bool> Boolean(bool value) => new(UiValue.FromBoolean(value));
    public static UiBinding<double> Number(double value) => new(UiValue.FromNumber(value));
    public static UiBinding<ImmutableArray<UiListItem>> Items(ImmutableArray<UiListItem> value) => new(UiValue.FromItems(value));
}

/// <summary>Local method bodies are lowered at build time; this method is never executed by the host.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiLocalHandlerAttribute : Attribute
{
    public bool GenerateKernel { get; set; } = true;
}

/// <summary>Worker-only C# handler; route it through an explicitly registered typed RPC endpoint.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiRemoteHandlerAttribute(int endpointId) : Attribute
{
    public int EndpointId { get; } = endpointId;
    public bool GenerateEndpoint { get; set; } = true;
}
