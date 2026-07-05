using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class SubscriptionDeliveryMidCancellationTests
{
    private sealed record Ping(string Id);

    [Fact]
    public async Task Canceled_filter_delivery_stops_before_later_filters_and_handlers()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var firstFilterInvoked = false;
        var secondFilterInvoked = false;
        var handlerInvoked = false;

        Func<Ping, HookContext, ValueTask<bool>>[] filters =
        [
            (_, _) =>
            {
                firstFilterInvoked = true;
                cancellation.Cancel();
                return ValueTask.FromResult(true);
            },
            (_, _) =>
            {
                secondFilterInvoked = true;
                return ValueTask.FromResult(true);
            }
        ];
        Func<Ping, HookContext, HookContext, ValueTask>[] handlers =
        [
            (_, _, _) =>
            {
                handlerInvoked = true;
                return ValueTask.CompletedTask;
            }
        ];

        await SubscriptionDelivery.PublishSafelyAsync(
            filters,
            handlers,
            new Ping("probe"),
            context,
            context,
            onFault: null);

        Assert.True(firstFilterInvoked);
        Assert.False(secondFilterInvoked);
        Assert.False(handlerInvoked);
    }
}
