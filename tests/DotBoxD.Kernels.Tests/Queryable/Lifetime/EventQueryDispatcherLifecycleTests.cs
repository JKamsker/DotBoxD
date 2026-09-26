using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryDispatcherLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resubscription_preserves_event_counters_while_other_event_types_remain_active(bool indexed)
    {
        var host = new EventQueryHost();
        var calls = 0;
        var query = indexed ? host.Query<Sample>().Where(e => e.Value == 1) : host.Query<Sample>();
        using var other = await host.Query<OtherSample>().SubscribeAsync((_, _) =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });
        var first = await query.SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
        await host.PublishAsync(new Sample(1), Context());
        first.Dispose();
        Assert.False(host.HasSubscriptions<Sample>());
        Assert.True(host.HasSubscriptions<OtherSample>());
        await host.PublishAsync(new Sample(1), Context());
        await host.PublishAsync(new OtherSample(), Context());

        using var second = await query.SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
        Assert.Equal(1, second.EventsObserved);
        await host.PublishAsync(new Sample(1), Context());

        Assert.Equal(2, first.EventsObserved);
        Assert.Equal(2, second.EventsObserved);
        Assert.Equal(1, first.Dispatches);
        Assert.Equal(1, second.Dispatches);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Last_unsubscription_racing_registration_cannot_drop_the_new_subscription(bool indexed)
    {
        var host = new EventQueryHost();
        var query = indexed ? host.Query<Sample>().Where(e => e.Value == 1) : host.Query<Sample>();
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var previous = await query.SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispose = Task.Run(async () =>
            {
                await start.Task;
                previous.Dispose();
            });
            var register = Task.Run(async () =>
            {
                await start.Task;
                return await query.SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
            });
            start.SetResult();
            await Task.WhenAll(dispose, register).WaitAsync(TimeSpan.FromSeconds(5));
            using var current = await register;

            Assert.True(host.HasSubscriptions<Sample>());
            await host.PublishAsync(new Sample(1), Context());
            Assert.Equal(1, current.Dispatches);
            Assert.Equal(0, previous.Dispatches);
        }

        Assert.False(host.HasSubscriptions<Sample>());
    }

    [Fact]
    public async Task A_handler_can_replace_its_own_last_subscription()
    {
        var host = new EventQueryHost();
        EventQuerySubscriptionHandle first = null!;
        EventQuerySubscriptionHandle? second = null;
        first = await host.Query<Sample>().SubscribeAsync(async (_, _) =>
        {
            first.Dispose();
            second = await host.Query<Sample>().SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
        });

        await host.PublishAsync(new Sample(1), Context());
        using var replacement = Assert.IsType<EventQuerySubscriptionHandle>(second);
        Assert.Equal(1, first.Dispatches);
        Assert.Equal(0, replacement.Dispatches);
        await host.PublishAsync(new Sample(1), Context());
        Assert.Equal(1, first.Dispatches);
        Assert.Equal(1, replacement.Dispatches);
        Assert.Equal(2, replacement.EventsObserved);
    }

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);
    private sealed record Sample(int Value);
    private sealed record OtherSample;
}
