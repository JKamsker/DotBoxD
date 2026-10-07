namespace DotBoxD.UI.Runtime;

/// <summary>Trusted renderer input queue. Implementations must bound queued input and cancel reads.</summary>
public interface IUiInputSource
{
    ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Exactly one event ID or registered two-way node/property/value input.</summary>
public sealed record UiInput(int EventId = 0, int NodeId = 0, UiPropertyId PropertyId = default, UiValue? Value = null);
