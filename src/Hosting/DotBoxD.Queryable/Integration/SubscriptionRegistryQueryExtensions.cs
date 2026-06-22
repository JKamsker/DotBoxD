using System.Runtime.CompilerServices;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Integration;

namespace DotBoxD.Queryable;

/// <summary>
/// Opt-in integration that adds a dynamic <c>Query&lt;TEvent&gt;()</c> surface to a
/// <see cref="SubscriptionRegistry"/>, alongside the existing analyzer-lowered
/// <c>On&lt;TEvent&gt;().Where(...).Select(...).Run(...)</c> path. Each registry gets one
/// <see cref="EventQueryHost"/>; the first query for an event type installs a single forwarding
/// subscription so dynamic queries are dispatched through the host's index rather than fanned out.
/// </summary>
public static class SubscriptionRegistryQueryExtensions
{
    private static readonly ConditionalWeakTable<SubscriptionRegistry, RegistryQueryBinding> Bindings = new();

    /// <summary>
    /// Begins a dynamic event query over <typeparamref name="TEvent"/> on this registry. The first query for
    /// an event type installs a single forwarding subscription, so events reach the query host's index. For a
    /// non-registry event source, construct an <see cref="EventQueryHost"/> directly and feed it with
    /// <see cref="EventQueryHost.PublishAsync{TEvent}"/>.
    /// </summary>
    public static EventQuery<TEvent> Query<TEvent>(this SubscriptionRegistry subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        var binding = Bindings.GetValue(subscriptions, static registry => new RegistryQueryBinding(registry));
        binding.EnsureForwarder<TEvent>();
        return binding.Host.Query<TEvent>();
    }
}
