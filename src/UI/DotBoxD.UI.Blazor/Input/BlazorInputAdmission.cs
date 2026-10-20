using System.Diagnostics;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor;

internal sealed class BlazorInputAdmission(UiPolicy policy)
{
    private long _window = Stopwatch.GetTimestamp();
    private int _events;
    private int _pending;

    // Called under the renderer lock. Bound work before awaiting any host authorization service.
    public void Begin()
    {
        if (Stopwatch.GetElapsedTime(_window) >= TimeSpan.FromSeconds(1))
        { _window = Stopwatch.GetTimestamp(); _events = 0; }
        if (++_events > policy.MaxInputEventsPerSecond || Volatile.Read(ref _pending) >= policy.MaxInFlightRemoteEvents)
        { throw new UiValidationException("Blazor input rate or authorization concurrency limit exceeded."); }
        Interlocked.Increment(ref _pending);
    }

    public void End() => Interlocked.Decrement(ref _pending);

    public static void Validate(BlazorUiSnapshot snapshot, UiInput input, UiPolicy policy)
    {
        if (IsEvent(snapshot, input))
        { return; }
        if (input.EventId != 0 || input.Value is null || !snapshot.Nodes.TryGetValue(input.NodeId, out var node) ||
            !node.Properties.Any(p => p.Id == input.PropertyId && p.TwoWay))
        { throw new UiValidationException("Browser input must address a declared event or two-way property."); }
        UiValueValidator.Validate(input.Value, policy);
        if (input.Value.Kind != UiValueValidator.PropertyKind(node.Primitive, input.PropertyId))
        { throw new UiValidationException("Browser input property type mismatch."); }
    }

    private static bool IsEvent(BlazorUiSnapshot snapshot, UiInput input)
        => input.EventId > 0 && input.NodeId == 0 && input.PropertyId == default && input.Value is null &&
            snapshot.Package.Events.Any(e => e.Id == input.EventId);
}
