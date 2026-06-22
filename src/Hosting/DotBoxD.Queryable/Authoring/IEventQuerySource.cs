namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// The opt-in entry point for dynamic event queries. A source hands out a restricted query builder per
/// event type; this is deliberately not arbitrary LINQ — only the shapes the portable model supports are
/// accepted, and unsupported expressions fail fast at subscribe time.
/// </summary>
public interface IEventQuerySource
{
    /// <summary>Begins a query over events of type <typeparamref name="TEvent"/>.</summary>
    EventQuery<TEvent> Query<TEvent>();
}
