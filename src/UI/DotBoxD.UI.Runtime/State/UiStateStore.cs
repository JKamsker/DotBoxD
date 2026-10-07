using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

internal sealed class UiStateStore(UiPackage package, UiPolicy policy)
{
    private ImmutableDictionary<int, UiValue> _state = package.State.ToImmutableDictionary(s => s.Id, s => s.InitialValue);

    public long Version { get; private set; }
    public IReadOnlyDictionary<int, UiValue> Values => _state;

    public UiSnapshot Snapshot(Guid sessionId)
        => new(sessionId, Version, [.. _state.OrderBy(s => s.Key).Select(s => new UiStateValue(s.Key, s.Value))]);

    public ImmutableDictionary<int, UiValue> Stage(ImmutableArray<UiStateValue> writes)
    {
        if (writes.IsDefault || writes.Length > policy.MaxPatchSlots)
        {
            throw new UiValidationException("UI patch exceeds the host slot limit or is missing writes.");
        }

        var next = _state.ToBuilder();
        var seen = new HashSet<int>();
        foreach (var write in writes)
        {
            StageWrite(next, seen, write);
        }

        long bytes = 0;
        foreach (var value in next.Values)
        {
            bytes += UiValueValidator.Validate(value, policy);
        }

        if (bytes > policy.MaxStateBytes)
        {
            throw new UiValidationException("UI patch exceeds the host state byte limit.");
        }

        return next.ToImmutable();
    }

    private void StageWrite(ImmutableDictionary<int, UiValue>.Builder next, HashSet<int> seen, UiStateValue write)
    {
        if (write is null || !seen.Add(write.SlotId) || !next.TryGetValue(write.SlotId, out var previous) ||
            write.Value is null || previous.Kind != write.Value.Kind)
        {
            throw new UiValidationException("UI patch contains duplicate, unknown or incompatible slots.");
        }

        UiValueValidator.Validate(write.Value, policy);
        next[write.SlotId] = write.Value;
    }

    public void Commit(ImmutableDictionary<int, UiValue> next)
    {
        var version = checked(Version + 1);
        _state = next;
        Version = version;
    }

    public void Clear() => _state = ImmutableDictionary<int, UiValue>.Empty;
}
