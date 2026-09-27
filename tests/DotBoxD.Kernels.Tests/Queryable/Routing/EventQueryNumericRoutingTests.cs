using System.Linq.Expressions;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryNumericRoutingTests
{
    [Fact]
    public Task SignedMemberComparedAsDecimal_ReachesMatchingSubscription() =>
        AssertMatchesAsync<SignedEvent>(e => (decimal)e.Value == 5m,
            [new(4), new(5), new(6)]);

    [Fact]
    public Task UnsignedMemberComparedAsDecimal_ReachesMatchingSubscription() =>
        AssertMatchesAsync<UnsignedEvent>(e => (decimal)e.Value == 5m,
            [new(4), new(5), new(6)]);

    [Fact]
    public Task SmallUnsignedMemberComparedAsUlong_ReachesMatchingSubscription() =>
        AssertMatchesAsync<SmallUnsignedEvent>(e => (ulong)e.Value == 5UL,
            [new(4), new(5), new(6)]);

    [Fact]
    public Task DecimalScaleAndPrecision_ArePreserved() =>
        AssertMatchesAsync<DecimalEvent>(e => e.Value == 9007199254740993m,
            [new(9007199254740992m), new(9007199254740993m), new(9007199254740993.00m)]);

    [Fact]
    public Task LargeUnsignedPrecision_IsPreserved() =>
        AssertMatchesAsync<UnsignedEvent>(e => e.Value == ulong.MaxValue,
            [new(ulong.MaxValue - 1), new(ulong.MaxValue)]);

    [Fact]
    public Task CompositeNumericAndTextKeys_ReachMatchingSubscription() =>
        AssertMatchesAsync<CompositeEvent>(e => (decimal)e.Value == 5m && e.Category == "selected",
            [new(5, "other"), new(4, "selected"), new(5, "selected")]);

    [Fact]
    public Task EqualDecimalsWhoseDoubleConversionsDiffer_ReachMatchingSubscription() =>
        AssertMatchesAsync<DecimalEvent>(e => e.Value == 2421988103042415229m,
            [new(2421988103042415229m), new(2421988103042415229.000000000m)]);

    [Fact]
    public Task UnsignedMaximumComparedAsDecimal_PreservesPrecision() =>
        AssertMatchesAsync<UnsignedEvent>(e => (decimal)e.Value == 18446744073709551615m,
            [new(ulong.MaxValue - 1), new(ulong.MaxValue)]);

    [Fact]
    public Task NullableMemberComparedAsDecimal_PreservesNullSemantics() =>
        AssertMatchesAsync<NullableEvent>(e => (decimal?)e.Value == 5m,
            [new(null), new(4), new(5), new(6)]);

    [Fact]
    public Task FloatingMemberComparedWithCapturedInteger_ReachesMatchingSubscription()
    {
        var expected = 5;
        return AssertMatchesAsync<FloatingEvent>(e => e.Value == expected,
            [new(4.5), new(5), new(5.5)]);
    }

    [Fact]
    public async Task ExactAndFloatingComparisonsOnTheSamePath_UseTheirOwnDomains()
    {
        var host = new EventQueryHost();
        var exactHits = 0;
        var floatingHits = 0;
        using var exact = await host.Query<CompositeEvent>()
            .Where(e => (decimal)e.Value == 5m)
            .SubscribeAsync((_, _) => { exactHits++; return ValueTask.CompletedTask; });
        using var floating = await host.Query<CompositeEvent>()
            .Where(e => (double)e.Value == 5d)
            .SubscribeAsync((_, _) => { floatingHits++; return ValueTask.CompletedTask; });
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);

        await host.PublishAsync(new CompositeEvent(4, "a"), context);
        await host.PublishAsync(new CompositeEvent(5, "a"), context);
        await host.PublishAsync(new CompositeEvent(6, "a"), context);

        Assert.Equal(1, exactHits);
        Assert.Equal(1, floatingHits);
        Assert.Equal(1, exact.FilterEvaluations);
        Assert.Equal(1, floating.FilterEvaluations);
    }

    private static async Task AssertMatchesAsync<TEvent>(
        Expression<Func<TEvent, bool>> predicate,
        TEvent[] events)
    {
        var host = new EventQueryHost();
        var received = new List<TEvent>();
        using var handle = await host.Query<TEvent>().Where(predicate).SubscribeAsync((value, _) =>
        {
            received.Add(value);
            return ValueTask.CompletedTask;
        });
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        foreach (var value in events)
        {
            await host.PublishAsync(value, context);
        }

        Assert.True(handle.Plan.IsRoutable);
        Assert.Equal(events.Where(predicate.Compile()), received);
    }

    private sealed record SignedEvent(long Value);
    private sealed record UnsignedEvent(ulong Value);
    private sealed record SmallUnsignedEvent(uint Value);
    private sealed record DecimalEvent(decimal Value);
    private sealed record NullableEvent(int? Value);
    private sealed record FloatingEvent(double Value);
    private sealed record CompositeEvent(int Value, string Category);
}
