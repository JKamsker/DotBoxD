using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// Routes events of one type to matching query subscriptions. Subscriptions with an equality routing key
/// are indexed; an incoming event probes the index by its member values, so only subscriptions whose
/// indexed equality the event satisfies become candidates. Each candidate's residual filter is then
/// interpreted from the portable AST and, on a match, the projection is materialized and dispatched.
/// Subscriptions without a routing key are evaluated against every event (an explicit broad fallback).
/// </summary>
internal sealed class EventQueryDispatcher<TEvent>(MemberValueReader reader)
{
    private readonly object _gate = new();
    private long _eventsObserved;
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public long EventsObserved => Interlocked.Read(ref _eventsObserved);

    public EventQuerySubscriptionHandle Register(
        EventQueryDocument document,
        EventQueryPlan plan,
        Func<TEvent, object?> project,
        Func<object?, HookContext, ValueTask> dispatch)
    {
        QueryFilterEvaluator.EnsureWithinLimits(document.Filter);
        var fingerprint = QueryFingerprint.Compute(document);

        Entry entry = null!;
        var handle = new EventQuerySubscriptionHandle(
            document, plan, fingerprint, () => EventsObserved, () => Remove(entry));
        entry = new Entry(document.Filter, RoutingKeyFor(plan), project, dispatch, handle);

        lock (_gate)
        {
            _snapshot = _snapshot.With(entry);
        }

        return handle;
    }

    public async ValueTask PublishAsync(TEvent e, HookContext context)
    {
        Interlocked.Increment(ref _eventsObserved);
        var snapshot = _snapshot;
        if (snapshot.IsEmpty || e is null)
        {
            return;
        }

        foreach (var entry in snapshot.Candidates(e, reader))
        {
            entry.Handle.RecordFilterEvaluation();
            if (!TryEvaluate(entry, e))
            {
                continue;
            }

            entry.Handle.RecordMatch();
            if (!TryProject(entry, e, out var projected))
            {
                continue;
            }

            await entry.Dispatch(projected, context).ConfigureAwait(false);
            entry.Handle.RecordDispatch();
        }
    }

    private bool TryEvaluate(Entry entry, TEvent e)
    {
        try
        {
            return QueryFilterEvaluator.Evaluate(entry.Filter, e!, reader);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryProject(Entry entry, TEvent e, out object? projected)
    {
        try
        {
            projected = entry.Project(e);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or NullReferenceException)
        {
            projected = null;
            return false;
        }
    }

    private void Remove(Entry entry)
    {
        lock (_gate)
        {
            _snapshot = _snapshot.Without(entry);
        }
    }

    private static EventQueryRoutingKey? RoutingKeyFor(EventQueryPlan plan)
        => plan.RoutingKeys.Count > 0
            ? EventQueryRoutingKey.FromValue(plan.RoutingKeys[0].Path, plan.RoutingKeys[0].Value)
            : null;

    private sealed class Entry(
        QueryFilter filter,
        EventQueryRoutingKey? routingKey,
        Func<TEvent, object?> project,
        Func<object?, HookContext, ValueTask> dispatch,
        EventQuerySubscriptionHandle handle)
    {
        public QueryFilter Filter { get; } = filter;

        public EventQueryRoutingKey? RoutingKey { get; } = routingKey;

        public Func<TEvent, object?> Project { get; } = project;

        public Func<object?, HookContext, ValueTask> Dispatch { get; } = dispatch;

        public EventQuerySubscriptionHandle Handle { get; } = handle;
    }

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new([]);

        private readonly Entry[] _all;
        private readonly Entry[] _broad;
        private readonly string[] _routingPaths;
        private readonly Dictionary<EventQueryRoutingKey, List<Entry>> _index;

        private Snapshot(Entry[] all)
        {
            _all = all;
            var broad = new List<Entry>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            _index = [];
            foreach (var entry in all)
            {
                if (entry.RoutingKey is { } key)
                {
                    paths.Add(key.Path);
                    if (!_index.TryGetValue(key, out var bucket))
                    {
                        bucket = [];
                        _index[key] = bucket;
                    }

                    bucket.Add(entry);
                }
                else
                {
                    broad.Add(entry);
                }
            }

            _broad = [.. broad];
            _routingPaths = [.. paths];
        }

        public bool IsEmpty => _all.Length == 0;

        public Snapshot With(Entry entry) => new([.. _all, entry]);

        public Snapshot Without(Entry entry) => new(_all.Where(e => !ReferenceEquals(e, entry)).ToArray());

        public IEnumerable<Entry> Candidates(TEvent e, MemberValueReader reader)
        {
            var matched = new List<Entry>(_broad);
            foreach (var path in _routingPaths)
            {
                if (EventQueryRoutingKey.TryFromRuntime(path, reader.Read(e!, path), out var key) &&
                    _index.TryGetValue(key, out var bucket))
                {
                    matched.AddRange(bucket);
                }
            }

            return matched;
        }
    }
}
