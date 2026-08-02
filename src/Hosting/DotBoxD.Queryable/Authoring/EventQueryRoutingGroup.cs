namespace DotBoxD.Queryable.Authoring;

internal sealed class EventQueryRoutingGroup<TEvent>(string[] paths)
{
    private readonly Dictionary<string, List<EventQuerySubscriptionEntry<TEvent>>> _byValue =
        new(StringComparer.Ordinal);

    public string[] Paths { get; } = paths;

    public void Add(string compositeKey, EventQuerySubscriptionEntry<TEvent> entry)
    {
        if (!_byValue.TryGetValue(compositeKey, out var bucket))
        {
            bucket = [];
            _byValue[compositeKey] = bucket;
        }

        bucket.Add(entry);
    }

    public bool TryGet(string compositeKey, out List<EventQuerySubscriptionEntry<TEvent>> bucket)
        => _byValue.TryGetValue(compositeKey, out bucket!);
}
