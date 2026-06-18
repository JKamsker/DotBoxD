namespace DotBoxD.Queryable.Ast;

/// <summary>
/// The portable, host-visible representation of a captured event query: the source event type, the
/// translated filter AST, and the translated projection AST. This is the durable contract a host
/// preserves, indexes, logs, fingerprints, and replays — it deliberately carries no CLR
/// <see cref="System.Linq.Expressions.Expression"/> so it survives across process and runtime boundaries.
/// </summary>
public sealed record EventQueryDocument
{
    /// <summary>The fully-qualified name of the source event type the query observes.</summary>
    public required string EventName { get; init; }

    /// <summary>The filter AST; <see cref="QueryFilter.MatchAll"/> when the query has no predicate.</summary>
    public required QueryFilter Filter { get; init; }

    /// <summary>The projection AST describing the shape dispatched to the subscriber.</summary>
    public required QueryProjection Projection { get; init; }

    /// <summary>Builds a document from its parts, defaulting a missing filter to <see cref="QueryFilter.MatchAll"/>.</summary>
    public static EventQueryDocument Create(
        string eventName,
        QueryFilter? filter,
        QueryProjection projection)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        ArgumentNullException.ThrowIfNull(projection);
        return new EventQueryDocument
        {
            EventName = eventName,
            Filter = filter ?? QueryFilter.MatchAll,
            Projection = projection,
        };
    }
}
