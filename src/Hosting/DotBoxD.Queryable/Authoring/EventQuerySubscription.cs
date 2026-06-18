using System.Linq.Expressions;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// A query that has fixed its projection shape (<typeparamref name="TProjection"/>) and awaits a terminal
/// <see cref="SubscribeAsync"/>. The accumulated predicates and the projection are translated into the
/// portable AST when the subscription is registered.
/// </summary>
public sealed class EventQuerySubscription<TEvent, TProjection>
{
    private readonly EventQueryHost _host;
    private readonly IReadOnlyList<Expression<Func<TEvent, bool>>> _predicates;
    private readonly Expression<Func<TEvent, TProjection>> _projection;

    internal EventQuerySubscription(
        EventQueryHost host,
        IReadOnlyList<Expression<Func<TEvent, bool>>> predicates,
        Expression<Func<TEvent, TProjection>> projection)
    {
        _host = host;
        _predicates = predicates;
        _projection = projection;
    }

    /// <summary>Registers the query, dispatching the projected payload to <paramref name="handler"/>.</summary>
    public ValueTask<EventQuerySubscriptionHandle> SubscribeAsync(Func<TProjection, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var handle = _host.Register(_predicates, _projection, handler);
        return ValueTask.FromResult(handle);
    }
}
