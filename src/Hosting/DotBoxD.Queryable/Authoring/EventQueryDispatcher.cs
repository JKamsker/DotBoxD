using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
namespace DotBoxD.Queryable.Authoring;
/// <summary>
/// Routes events of one type to matching query subscriptions. Subscriptions with equality predicates are
/// indexed by a <em>composite</em> key over all their equality members, so an event becomes a candidate
/// only when it matches every indexed equality at once (more selective than a single-key index). Each
/// candidate's filter — including any residual/range predicates — is then evaluated (interpreted, promoting
/// to compiled when hot) and, on a match, the projection is materialized and dispatched. Subscriptions with
/// no equality predicate are evaluated against every event (an explicit broad fallback).
/// </summary>
internal sealed class EventQueryDispatcher<TEvent>(MemberValueReader reader)
{
    private readonly object _gate = new();
    private long _eventsObserved;
    private volatile EventQueryDispatcherSnapshot<TEvent> _snapshot = EventQueryDispatcherSnapshot<TEvent>.Empty;
    public long EventsObserved => Interlocked.Read(ref _eventsObserved);
    public bool HasSubscriptions => !_snapshot.IsEmpty;
    public EventQuerySubscriptionHandle Register(
        EventQueryDocument document,
        EventQueryPlan plan,
        Func<TEvent, object?> project,
        Func<object?, HookContext, ValueTask> dispatch)
    {
        QueryFilterEvaluator.EnsureWithinLimits(document.Filter);
        var fingerprint = QueryFingerprint.Compute(document);
        var routingKeys = RoutingKeysFor(plan);
        EventQuerySubscriptionEntry<TEvent> entry = null!;
        var handle = new EventQuerySubscriptionHandle(
            document, plan, fingerprint, () => EventsObserved, () => entry.IsCompiled, () => Remove(entry));
        entry = new EventQuerySubscriptionEntry<TEvent>(document.Filter, routingKeys, project, dispatch, handle);
        lock (_gate)
        {
            _snapshot = _snapshot.With(entry);
        }
        return handle;
    }
    public async ValueTask PublishAsync(TEvent e, HookContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _eventsObserved);
        var snapshot = _snapshot;
        if (snapshot.IsEmpty || e is null)
        {
            return;
        }
        foreach (var entry in snapshot.Broad)
        {
            await DispatchCandidateAsync(entry, e, context).ConfigureAwait(false);
        }
        foreach (var group in snapshot.Groups)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!EventQueryDispatcherSnapshot<TEvent>.TryEventKey(group.Paths, e, reader, context.CancellationToken, out var key))
            {
                continue;
            }
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!group.TryGet(key, out var bucket))
            {
                continue;
            }
            foreach (var entry in bucket)
            {
                await DispatchCandidateAsync(entry, e, context).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask DispatchCandidateAsync(
        EventQuerySubscriptionEntry<TEvent> entry,
        TEvent e,
        HookContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (entry.Handle.IsDisposed)
        {
            return;
        }

        entry.Handle.RecordFilterEvaluation();
        if (!TryEvaluate(entry, e, context.CancellationToken))
        {
            return;
        }

        if (entry.Handle.IsDisposed)
        {
            return;
        }

        entry.Handle.RecordMatch();
        if (!TryProject(entry, e, context.CancellationToken, out var projected))
        {
            return;
        }
        context.CancellationToken.ThrowIfCancellationRequested();
        if (entry.Handle.IsDisposed)
        {
            return;
        }

        try
        {
            await entry.Dispatch(projected, context).ConfigureAwait(false);
            entry.Handle.RecordDispatch();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(null, ex, context.CancellationToken);
        }
        catch
        {
            // Isolate one subscriber's handler failure so it cannot starve the other dynamic queries
            // matching this event — they share a single forwarding host handler at the registry layer.
        }
    }
    private bool TryEvaluate(EventQuerySubscriptionEntry<TEvent> entry, TEvent e, CancellationToken cancellationToken)
    {
        try
        {
            var matches = entry.Matches(e, reader);
            cancellationToken.ThrowIfCancellationRequested();
            return matches;
        }
        catch (InvalidOperationException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(null, ex, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
    private static bool TryProject(
        EventQuerySubscriptionEntry<TEvent> entry,
        TEvent e,
        CancellationToken cancellationToken,
        out object? projected)
    {
        try
        {
            projected = entry.Project(e);
            return true;
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(null, ex, cancellationToken);
        }
        catch
        {
            projected = null;
            return false;
        }
    }

    private void Remove(EventQuerySubscriptionEntry<TEvent> entry)
    {
        lock (_gate)
        {
            _snapshot = _snapshot.Without(entry);
        }
    }

    private static IReadOnlyList<EventQueryRoutingKey> RoutingKeysFor(EventQueryPlan plan)
    {
        if (plan.RoutingKeys.Count == 0)
        {
            return [];
        }

        var keys = new List<EventQueryRoutingKey>(plan.RoutingKeys.Count);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var predicate in plan.RoutingKeys)
        {
            if (seenPaths.Add(predicate.Path))
            {
                keys.Add(EventQueryRoutingKey.FromValue(predicate.Path, predicate.Value));
            }
        }

        return keys;
    }
}
