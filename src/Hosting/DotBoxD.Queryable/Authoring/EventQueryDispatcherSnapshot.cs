using System.Text;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Queryable.Authoring;

internal sealed class EventQueryDispatcherSnapshot<TEvent>
{
    public static readonly EventQueryDispatcherSnapshot<TEvent> Empty = new([]);

    private const string Separator = "\u0001";
    private const int MaxRetainedKeyCapacity = 1024;

    private readonly EventQuerySubscriptionEntry<TEvent>[] _all;
    private readonly EventQuerySubscriptionEntry<TEvent>[] _broad;
    private readonly EventQueryRoutingGroup<TEvent>[] _groups;

    private EventQueryDispatcherSnapshot(EventQuerySubscriptionEntry<TEvent>[] all)
    {
        _all = all;
        var broad = new List<EventQuerySubscriptionEntry<TEvent>>();
        var builders = new Dictionary<string, EventQueryRoutingGroup<TEvent>>(StringComparer.Ordinal);
        foreach (var entry in all)
        {
            if (!entry.IsRoutable)
            {
                broad.Add(entry);
                continue;
            }

            var paths = entry.RoutingKeys
                .Select(k => new EventQueryRoutingPath(k.Path, k.NumericRouting))
                .OrderBy(p => p.Path, StringComparer.Ordinal)
                .ToArray();

            var groupKey = string.Join(Separator, paths.Select(p => $"{p.NumericRouting}:{p.Path}"));
            if (!builders.TryGetValue(groupKey, out var group))
            {
                group = new EventQueryRoutingGroup<TEvent>(paths);
                builders[groupKey] = group;
            }

            group.Add(CompositeKey(entry, paths), entry);
        }

        _broad = [.. broad];
        _groups = [.. builders.Values];
    }

    public bool IsEmpty => _all.Length == 0;

    public EventQuerySubscriptionEntry<TEvent>[] Broad => _broad;
    public EventQueryRoutingGroup<TEvent>[] Groups => _groups;
    public EventQueryDispatcherSnapshot<TEvent> With(EventQuerySubscriptionEntry<TEvent> entry) => new([.. _all, entry]);
    public EventQueryDispatcherSnapshot<TEvent> Without(EventQuerySubscriptionEntry<TEvent> entry)
        => new(_all.Where(e => !ReferenceEquals(e, entry)).ToArray());

    // Reused on the hot TryEventKey path; nested same-thread calls allocate their own builder.
    [ThreadStatic] private static StringBuilder? _eventKeyBuilder;
    [ThreadStatic] private static bool _eventKeyBuilderInUse;

    private static string CompositeKey(EventQuerySubscriptionEntry<TEvent> entry, EventQueryRoutingPath[] sortedPaths)
    {
        var builder = new StringBuilder();
        foreach (var path in sortedPaths)
        {
            var key = entry.RoutingKeys.First(k => k.Path == path.Path);
            key.AppendValueToken(builder);
            builder.Append(Separator);
        }

        return builder.ToString();
    }

    public static bool TryEventKey(
        EventQueryRoutingPath[] sortedPaths,
        TEvent e,
        MemberValueReader reader,
        CancellationToken cancellationToken,
        out string key)
    {
        var reuseThreadBuilder = !_eventKeyBuilderInUse;
        var builder = reuseThreadBuilder ? _eventKeyBuilder ??= new StringBuilder() : new StringBuilder();
        if (reuseThreadBuilder)
        {
            _eventKeyBuilderInUse = true;
        }

        try
        {
            builder.Clear();
            foreach (var path in sortedPaths)
            {
                var value = reader.Read(e!, path.Path);
                cancellationToken.ThrowIfCancellationRequested();
                if (!EventQueryRoutingKey.TryFromRuntime(path.Path, value, path.NumericRouting, out var runtimeKey))
                {
                    key = string.Empty;
                    return false;
                }

                runtimeKey.AppendValueToken(builder);
                builder.Append(Separator);
            }

            key = builder.ToString();
            return true;
        }
        catch (InvalidOperationException)
        {
            key = string.Empty;
            return false;
        }
        finally
        {
            if (reuseThreadBuilder)
            {
                // Bound the buffer retained per thread/event type after a transient large key.
                // Discard before Clear, which can consolidate large chunks into another large buffer.
                if (builder.Capacity > MaxRetainedKeyCapacity)
                {
                    _eventKeyBuilder = null;
                }
                else
                {
                    builder.Clear();
                }

                _eventKeyBuilderInUse = false;
            }
        }
    }
}
