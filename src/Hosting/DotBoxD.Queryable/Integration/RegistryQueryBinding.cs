using DotBoxD.Plugins.Runtime;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Queryable.Integration;

/// <summary>
/// Binds an <see cref="EventQueryHost"/> to a <see cref="SubscriptionRegistry"/>. The first query for an
/// event type installs a single forwarding subscription on the registry that feeds every event of that
/// type into the host; the host's index then decides which dynamic queries actually fire, so the registry
/// sees one host handler per type rather than one per query.
/// </summary>
internal sealed class RegistryQueryBinding(SubscriptionRegistry registry)
{
    private readonly object _gate = new();
    private readonly HashSet<Type> _forwarded = [];

    /// <summary>The query host bound to the registry.</summary>
    public EventQueryHost Host { get; } = new();

    /// <summary>Installs the per-type forwarding subscription exactly once.</summary>
    public void EnsureForwarder<TEvent>()
    {
        lock (_gate)
        {
            if (!_forwarded.Add(typeof(TEvent)))
            {
                return;
            }

            registry.On<TEvent>().InvokeHostHandler((e, context) => Host.PublishAsync(e, context));
        }
    }
}
