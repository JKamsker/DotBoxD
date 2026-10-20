using System.Text;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Queryable.Authoring;

internal sealed class EventQueryDispatcherSnapshot<TEvent>
{
    public static readonly EventQueryDispatcherSnapshot<TEvent> Empty = new([], [], 0);

    private const string Separator = "\u0001";
    private const int MaxRetainedKeyCapacity = 1024;

    private readonly EventQuerySubscriptionEntry<TEvent>[] _broad;
    private readonly EventQueryRoutingGroup<TEvent>[] _groups;
    private readonly long _nextOrder;

    private EventQueryDispatcherSnapshot(EventQuerySubscriptionEntry<TEvent>[] broad, EventQueryRoutingGroup<TEvent>[] groups, long nextOrder)
    {
        _broad = broad;
        _groups = groups;
        _nextOrder = nextOrder;
    }

    public bool IsEmpty => _broad.Length == 0 && _groups.Length == 0;

    public EventQuerySubscriptionEntry<TEvent>[] Broad => _broad;
    public EventQueryRoutingGroup<TEvent>[] Groups => _groups;
    public EventQueryDispatcherSnapshot<TEvent> With(EventQuerySubscriptionEntry<TEvent> entry)
    {
        entry.RegistrationOrder = _nextOrder;
        if (!entry.IsRoutable)
        {
            return new([.. _broad, entry], _groups, _nextOrder + 1);
        }

        var (paths, groupKey, key) = Route(entry);
        var index = Array.FindIndex(_groups, group => group.Key == groupKey);
        if (index < 0)
        {
            return new(_broad, [.. _groups, new EventQueryRoutingGroup<TEvent>(groupKey, paths).With(key, entry)], _nextOrder + 1);
        }

        var groups = (EventQueryRoutingGroup<TEvent>[])_groups.Clone();
        groups[index] = groups[index].With(key, entry);
        return new(_broad, groups, _nextOrder + 1);
    }

    public EventQueryDispatcherSnapshot<TEvent> Without(EventQuerySubscriptionEntry<TEvent> entry)
    {
        if (!entry.IsRoutable)
        {
            return new(_broad.Where(candidate => !ReferenceEquals(candidate, entry)).ToArray(), _groups, _nextOrder);
        }

        var (_, groupKey, key) = Route(entry);
        var index = Array.FindIndex(_groups, group => group.Key == groupKey);
        var updated = _groups[index].Without(key, entry);
        var groups = (EventQueryRoutingGroup<TEvent>[])_groups.Clone();
        if (updated is null)
        {
            groups = groups.Where((_, i) => i != index).ToArray();
        }
        else
        {
            groups[index] = updated;
            Array.Sort(groups, static (left, right) => left.FirstRegistrationOrder.CompareTo(right.FirstRegistrationOrder));
        }

        return new(_broad, groups, _nextOrder);
    }

    private static (EventQueryRoutingPath[] Paths, string GroupKey, string Key) Route(EventQuerySubscriptionEntry<TEvent> entry)
    {
        var paths = entry.RoutingKeys.Select(key => new EventQueryRoutingPath(key.Path, key.NumericRouting))
            .OrderBy(path => path.Path, StringComparer.Ordinal).ToArray();
        var groupKey = string.Join(Separator, paths.Select(path => $"{path.NumericRouting}:{path.Path}"));
        return (paths, groupKey, CompositeKey(entry, paths));
    }

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
