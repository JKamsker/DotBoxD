using System.Collections.Immutable;

namespace DotBoxD.UI;

public sealed record UiStateValue(int SlotId, UiValue Value);

/// <summary>The host issues a fresh session ID on every installation.</summary>
public sealed record UiSnapshot(Guid SessionId, long Version, ImmutableArray<UiStateValue> State);

/// <summary>All writes succeed together only when session and expected version still match.</summary>
public sealed record UiStatePatch(Guid SessionId, long ExpectedVersion, ImmutableArray<UiStateValue> Writes);

/// <summary>Bounded scalar state snapshot addressed to an explicitly declared remote endpoint.</summary>
public sealed record UiRemoteEvent(int EventId, int NodeId, int EndpointId, UiSnapshot Snapshot);

public sealed record UiPropertyValue(int NodeId, UiPropertyId PropertyId, UiValue Value);
