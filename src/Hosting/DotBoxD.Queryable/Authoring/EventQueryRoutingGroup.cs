namespace DotBoxD.Queryable.Authoring;

internal sealed class EventQueryRoutingGroup<TEvent>
{
    private readonly Dictionary<string, List<EventQuerySubscriptionEntry<TEvent>>> _byValue;

    public EventQueryRoutingGroup(string key, EventQueryRoutingPath[] paths)
        : this(key, paths, new(StringComparer.Ordinal), long.MaxValue)
    {
    }

    private EventQueryRoutingGroup(string key, EventQueryRoutingPath[] paths,
        Dictionary<string, List<EventQuerySubscriptionEntry<TEvent>>> byValue, long firstRegistrationOrder)
    {
        Key = key;
        Paths = paths;
        _byValue = byValue;
        FirstRegistrationOrder = firstRegistrationOrder;
    }

    public string Key { get; }
    public EventQueryRoutingPath[] Paths { get; }
    public long FirstRegistrationOrder { get; }

    public EventQueryRoutingGroup<TEvent> With(string compositeKey, EventQuerySubscriptionEntry<TEvent> entry)
    {
        // Published groups and buckets are never mutated. Copy only the affected group
        // and bucket, sharing the remaining buckets with readers of the older snapshot.
        var byValue = new Dictionary<string, List<EventQuerySubscriptionEntry<TEvent>>>(_byValue, StringComparer.Ordinal);
        var bucket = _byValue.TryGetValue(compositeKey, out var previous)
            ? new List<EventQuerySubscriptionEntry<TEvent>>(previous.Count + 1)
            : [];
        if (previous is not null)
        {
            bucket.AddRange(previous);
        }

        bucket.Add(entry);
        byValue[compositeKey] = bucket;
        return new EventQueryRoutingGroup<TEvent>(Key, Paths, byValue, Math.Min(FirstRegistrationOrder, entry.RegistrationOrder));
    }

    public EventQueryRoutingGroup<TEvent>? Without(string compositeKey, EventQuerySubscriptionEntry<TEvent> entry)
    {
        var byValue = new Dictionary<string, List<EventQuerySubscriptionEntry<TEvent>>>(_byValue, StringComparer.Ordinal);
        var bucket = _byValue[compositeKey].Where(candidate => !ReferenceEquals(candidate, entry)).ToList();
        if (bucket.Count == 0)
        {
            byValue.Remove(compositeKey);
        }
        else
        {
            byValue[compositeKey] = bucket;
        }

        if (byValue.Count == 0)
        {
            return null;
        }

        var firstOrder = entry.RegistrationOrder == FirstRegistrationOrder
            ? byValue.Values.Min(entries => entries[0].RegistrationOrder)
            : FirstRegistrationOrder;
        return new EventQueryRoutingGroup<TEvent>(Key, Paths, byValue, firstOrder);
    }

    public bool TryGet(string compositeKey, out List<EventQuerySubscriptionEntry<TEvent>> bucket)
        => _byValue.TryGetValue(compositeKey, out bucket!);
}
