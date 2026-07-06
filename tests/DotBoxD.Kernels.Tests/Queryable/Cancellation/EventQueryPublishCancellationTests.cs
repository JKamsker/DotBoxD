using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryPublishCancellationTests
{
    [Fact]
    public async Task Mid_publish_cancellation_stops_before_later_subscription_work()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var firstInvocations = 0;
        var secondInvocations = 0;

        var first = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                firstInvocations++;
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            });
        var second = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                secondInvocations++;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await host.PublishAsync(new AttackTestEvent("player-1", "monster-1", 9, 1), context));

        Assert.Equal(1, firstInvocations);
        Assert.Equal(1, first.FilterEvaluations);
        Assert.Equal(1, first.Matches);
        Assert.Equal(1, first.Dispatches);

        Assert.Equal(0, secondInvocations);
        Assert.Equal(0, second.FilterEvaluations);
        Assert.Equal(0, second.Matches);
        Assert.Equal(0, second.Dispatches);
    }
}
