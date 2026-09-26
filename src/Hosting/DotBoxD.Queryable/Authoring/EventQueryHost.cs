using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Analysis;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// An in-process <see cref="IEventQuerySource"/>: it translates authored queries into the portable AST,
/// plans them, and dispatches events to matching subscriptions through a per-event-type indexed
/// <see cref="EventQueryDispatcher{TEvent}"/>. Feed it events with <see cref="PublishAsync{TEvent}"/>; a
/// host wires that call to its own event source (for example a subscription registry).
/// </summary>
public sealed class EventQueryHost : IEventQuerySource
{
    private readonly object _gate;
    private readonly Func<bool>? _isDisposed;
    // Active subscriptions strongly own their event types. Publishing still reads this map lock-free.
    private readonly ConcurrentDictionary<Type, object> _dispatchers = new();
    // Preserve counters across idle periods without pinning unused collectible event types.
    private readonly ConditionalWeakTable<Type, object> _dispatcherCache = new();

    /// <summary>Creates an independent in-process event-query host.</summary>
    public EventQueryHost()
        : this(isDisposed: null, lifecycleGate: null)
    {
    }

    internal EventQueryHost(Func<bool>? isDisposed, object? lifecycleGate)
    {
        _isDisposed = isDisposed;
        _gate = lifecycleGate ?? new object();
    }

    /// <inheritdoc />
    public EventQuery<TEvent> Query<TEvent>() => new(this);

    /// <summary>Routes an event to the matching query subscriptions registered for its type.</summary>
    public ValueTask PublishAsync<TEvent>(TEvent e, HookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(e);
        return TryGetDispatcher<TEvent>() is { } dispatcher
            ? dispatcher.PublishAsync(e, context)
            : ValueTask.CompletedTask;
    }

    /// <summary>Whether any subscription has been registered for <typeparamref name="TEvent"/>.</summary>
    public bool HasSubscriptions<TEvent>() => TryGetDispatcher<TEvent>() is not null;

    internal EventQuerySubscriptionHandle Register<TEvent, TProjection>(
        IReadOnlyList<Expression<Func<TEvent, bool>>> predicates,
        Expression<Func<TEvent, TProjection>>? projection,
        Func<TProjection, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_isDisposed?.Invoke() == true, this);

        var filter = BuildFilter(predicates);
        QuerySatisfiability.EnsureSatisfiable(filter);
        var (projectionAst, project) = BuildProjection(projection);
        var document = EventQueryDocument.Create(ExpressionQueryTranslator.EventName<TEvent>(), filter, projectionAst);
        var plan = EventQueryPlanner.Plan(document);

        ValueTask Dispatch(object? projected, HookContext context) => handler((TProjection)projected!, context);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed?.Invoke() == true, this);
            var dispatcher = GetOrAddDispatcher<TEvent>();
            var handle = dispatcher.Register(document, plan, project, Dispatch);
            _dispatchers.TryAdd(typeof(TEvent), dispatcher);
            return handle;
        }
    }

    private static QueryFilter BuildFilter<TEvent>(IReadOnlyList<Expression<Func<TEvent, bool>>> predicates)
    {
        if (predicates.Count == 0)
        {
            return QueryFilter.MatchAll;
        }

        var filters = new QueryFilter[predicates.Count];
        for (var i = 0; i < predicates.Count; i++)
        {
            filters[i] = ExpressionQueryTranslator.TranslateFilter(predicates[i]);
        }

        return QueryFilter.And(filters);
    }

    private static (QueryProjection Ast, Func<TEvent, object?> Project) BuildProjection<TEvent, TProjection>(
        Expression<Func<TEvent, TProjection>>? projection)
    {
        if (projection is null)
        {
            return (QueryProjection.Identity, e => e);
        }

        var ast = ExpressionQueryTranslator.TranslateProjection(projection);
        var compiled = projection.Compile();
        return (ast, e => compiled(e));
    }

    private EventQueryDispatcher<TEvent> GetOrAddDispatcher<TEvent>()
    {
        // Serialize creation and registration so every subscription for a type uses the same dispatcher,
        // including when the last existing subscription is concurrently removed from the active map.
        lock (_gate)
        {
            if (!_dispatcherCache.TryGetValue(typeof(TEvent), out var existing))
            {
                existing = new EventQueryDispatcher<TEvent>(
                    new MemberValueReader(typeof(TEvent)), _isDisposed, RemoveIdleDispatcher);
                _dispatcherCache.Add(typeof(TEvent), existing);
            }

            return (EventQueryDispatcher<TEvent>)existing;
        }
    }

    private void RemoveIdleDispatcher<TEvent>(EventQueryDispatcher<TEvent> dispatcher)
    {
        lock (_gate)
        {
            if (!dispatcher.HasSubscriptions &&
                _dispatchers.TryGetValue(typeof(TEvent), out var current) && ReferenceEquals(current, dispatcher))
            {
                _dispatchers.TryRemove(typeof(TEvent), out _);
            }
        }
    }

    private EventQueryDispatcher<TEvent>? TryGetDispatcher<TEvent>()
    {
        if (!_dispatchers.TryGetValue(typeof(TEvent), out var existing))
        {
            return null;
        }

        var dispatcher = (EventQueryDispatcher<TEvent>)existing;
        return dispatcher.HasSubscriptions ? dispatcher : null;
    }
}
