using DotBoxD.Abstractions;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryHostTests
{
    private static HookContext NewContext() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    [Fact]
    public async Task Indexed_dispatch_only_reaches_matching_subscriptions()
    {
        var host = new EventQueryHost();
        var notices = new List<AttackNotice>();
        var watchedAttacker = "player-1";
        var minimumDamage = 5;

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == watchedAttacker && e.Damage >= minimumDamage)
            .Select(e => new AttackNotice(e.AttackerId, e.TargetId, e.Damage))
            .SubscribeAsync((notice, _) =>
            {
                notices.Add(notice);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        foreach (var attack in BuildHundredEvents())
        {
            await host.PublishAsync(attack, context);
        }

        // Only the two player-1 attacks with damage >= 5 reach the handler.
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.Equal("player-1", n.AttackerId));
        Assert.All(notices, n => Assert.True(n.Damage >= minimumDamage));

        // The dispatcher saw all 100 events but the index prefiltered to the 3 player-1 events,
        // so the subscription's filter ran 3 times, not 100 — no broad fan-out.
        Assert.Equal(100, handle.EventsObserved);
        Assert.Equal(3, handle.FilterEvaluations);
        Assert.Equal(2, handle.Matches);
        Assert.Equal(2, handle.Dispatches);

        // The planner extracted both index constraints with full coverage.
        Assert.Equal(IndexCoverage.Full, handle.Plan.Coverage);
        Assert.Equal(2, handle.Plan.IndexedPredicates.Count);
        Assert.Single(handle.Plan.RoutingKeys);

        // The preserved query AST round-trips through serialization with a stable fingerprint.
        var restored = EventQueryJson.Deserialize(EventQueryJson.Serialize(handle.Document));
        Assert.Equal(handle.Fingerprint, QueryFingerprint.Compute(restored));
    }

    [Fact]
    public async Task Identity_projection_dispatches_the_event_itself()
    {
        var host = new EventQueryHost();
        var received = new List<AttackTestEvent>();

        await host.Query<AttackTestEvent>()
            .Where(e => e.Damage >= 5)
            .SubscribeAsync((e, _) =>
            {
                received.Add(e);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new AttackTestEvent("a", "b", 9, 1), context);
        await host.PublishAsync(new AttackTestEvent("a", "b", 1, 1), context);

        Assert.Single(received);
        Assert.Equal(9, received[0].Damage);
    }

    [Fact]
    public async Task Disposing_the_handle_stops_dispatch()
    {
        var host = new EventQueryHost();
        var count = 0;

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "a")
            .SubscribeAsync((_, _) =>
            {
                count++;
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new AttackTestEvent("a", "b", 1, 1), context);
        handle.Dispose();
        await host.PublishAsync(new AttackTestEvent("a", "b", 1, 1), context);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Residual_filter_is_evaluated_on_indexed_candidates()
    {
        var host = new EventQueryHost();
        var hits = new List<string>();

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1" && e.TargetId.StartsWith("monster-"))
            .Select(e => e.TargetId)
            .SubscribeAsync((target, _) =>
            {
                hits.Add(target);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new AttackTestEvent("player-1", "monster-1", 1, 1), context);
        await host.PublishAsync(new AttackTestEvent("player-1", "boss-1", 1, 1), context);
        await host.PublishAsync(new AttackTestEvent("player-2", "monster-2", 1, 1), context);

        Assert.Equal(new[] { "monster-1" }, hits);
        Assert.Equal(IndexCoverage.Partial, handle.Plan.Coverage);
        // Index routed only the two player-1 events to the residual filter.
        Assert.Equal(2, handle.FilterEvaluations);
    }

    [Fact]
    public async Task Null_equality_subscription_matches_events_with_a_null_member()
    {
        var host = new EventQueryHost();
        var matched = new List<NullableTestEvent>();

        var handle = await host.Query<NullableTestEvent>()
            .Where(e => e.Key == null)
            .SubscribeAsync((e, _) =>
            {
                matched.Add(e);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new NullableTestEvent(null, 1), context);
        await host.PublishAsync(new NullableTestEvent("set", 2), context);

        Assert.Single(matched);
        Assert.Null(matched[0].Key);
        // Null equality is not index-routable; it must fall back to broad evaluation.
        Assert.False(handle.Plan.IsRoutable);
        Assert.Equal(2, handle.FilterEvaluations);
    }

    [Fact]
    public async Task Numeric_equality_routes_across_integer_literal_and_floating_member()
    {
        var host = new EventQueryHost();
        var matched = new List<MetricTestEvent>();

        // Integer literal compared against a double member: the captured value is Integer while the runtime
        // member reads as a double — the routing key must still match.
        var handle = await host.Query<MetricTestEvent>()
            .Where(e => e.Score == 100)
            .SubscribeAsync((e, _) =>
            {
                matched.Add(e);
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        await host.PublishAsync(new MetricTestEvent("a", 100.0), context);
        await host.PublishAsync(new MetricTestEvent("b", 99.5), context);

        Assert.Single(matched);
        Assert.Equal("a", matched[0].Id);
        Assert.True(handle.Plan.IsRoutable);
    }

    [Fact]
    public async Task Throwing_getter_does_not_abort_dispatch_for_other_subscriptions()
    {
        var host = new EventQueryHost();
        var idHits = 0;

        await host.Query<ThrowingGetterEvent>()
            .Where(e => e.Boom == "never")
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        await host.Query<ThrowingGetterEvent>()
            .Where(e => e.Id == "x")
            .SubscribeAsync((_, _) =>
            {
                idHits++;
                return ValueTask.CompletedTask;
            });

        var context = NewContext();
        // Must not throw even though the first subscription's filter reads a getter that throws.
        await host.PublishAsync(new ThrowingGetterEvent("x"), context);

        Assert.Equal(1, idHits);
    }

    private static IEnumerable<AttackTestEvent> BuildHundredEvents()
    {
        // 3 attacks from player-1: damage 9, 3, 7 -> two are >= 5.
        yield return new AttackTestEvent("player-1", "monster-1", 9, 5);
        yield return new AttackTestEvent("player-1", "monster-2", 3, 5);
        yield return new AttackTestEvent("player-1", "monster-3", 7, 5);

        // 97 attacks from other attackers (most with damage >= 5 to prove the attacker index, not damage, prefilters).
        for (var i = 0; i < 97; i++)
        {
            yield return new AttackTestEvent($"player-{(i % 4) + 2}", "monster", 8, 4);
        }
    }
}
