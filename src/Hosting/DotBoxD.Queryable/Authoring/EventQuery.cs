using System.Linq.Expressions;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// A restricted, immutable query builder over events of type <typeparamref name="TEvent"/>. Each
/// <see cref="Where"/> returns a new builder with the predicate appended (predicates are conjoined);
/// <see cref="Select{TProjection}"/> fixes the projection shape; and the terminal subscribe operations
/// translate the captured expressions into the portable AST and register the subscription. Only the
/// expression shapes the model supports are accepted — unsupported shapes throw at subscribe time.
/// </summary>
/// <remarks>
/// This is the runtime-dynamic sibling of the source-generated hook/subscription pipeline. It takes runtime
/// <see cref="Expression"/> trees rather than source lambdas, translates them to a portable AST at subscribe
/// time, and dispatches in-process — so it is deliberately not part of the <c>[PipelineSurface]</c> /
/// <c>[IRBodyOf]</c> source-generator vocabulary. See <c>docs/design/event-query-vs-pipeline</c> for
/// when to use each.
/// </remarks>
public sealed class EventQuery<TEvent>
{
    private readonly EventQueryHost _host;
    private readonly IReadOnlyList<Expression<Func<TEvent, bool>>> _predicates;

    internal EventQuery(EventQueryHost host)
        : this(host, [])
    {
    }

    private EventQuery(EventQueryHost host, IReadOnlyList<Expression<Func<TEvent, bool>>> predicates)
    {
        _host = host;
        _predicates = predicates;
    }

    /// <summary>Adds a filter predicate (conjoined with any earlier predicates).</summary>
    public EventQuery<TEvent> Where(Expression<Func<TEvent, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new EventQuery<TEvent>(_host, [.. _predicates, predicate]);
    }

    /// <summary>Projects matched events to <typeparamref name="TProjection"/> before dispatch.</summary>
    public EventQuerySubscription<TEvent, TProjection> Select<TProjection>(
        Expression<Func<TEvent, TProjection>> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new EventQuerySubscription<TEvent, TProjection>(_host, _predicates, projection);
    }

    /// <summary>Registers the query with an identity projection, dispatching the matched event itself.</summary>
    public ValueTask<EventQuerySubscriptionHandle> SubscribeAsync(Func<TEvent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var handle = _host.Register<TEvent, TEvent>(_predicates, projection: null, handler);
        return ValueTask.FromResult(handle);
    }
}
