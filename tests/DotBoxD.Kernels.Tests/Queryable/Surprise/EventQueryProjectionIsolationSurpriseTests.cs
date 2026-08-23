using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryProjectionIsolationSurpriseTests
{
    [Fact]
    public async Task Ordinary_projection_fault_does_not_starve_later_subscriptions()
    {
        var host = new EventQueryHost();
        var secondDispatches = 0;

        var first = await host.Query<ProjectionFaultEvent>()
            .Select(@event => @event.ThrowingValue)
            .SubscribeAsync((_, _) => ValueTask.CompletedTask);
        await host.Query<ProjectionFaultEvent>()
            .SubscribeAsync((_, _) =>
            {
                secondDispatches++;
                return ValueTask.CompletedTask;
            });

        await host.PublishAsync(
            new ProjectionFaultEvent(),
            new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(1, first.Matches);
        Assert.Equal(0, first.Dispatches);
        Assert.Equal(1, secondDispatches);
    }

    private sealed class ProjectionFaultEvent
    {
        public int ThrowingValue => throw new InvalidTimeZoneException("projection failure");
    }
}
