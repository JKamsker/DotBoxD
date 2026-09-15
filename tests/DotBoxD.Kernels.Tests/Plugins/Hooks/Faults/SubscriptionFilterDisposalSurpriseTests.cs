using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class SubscriptionFilterDisposalSurpriseTests
{
    [Fact]
    public async Task Filter_disposal_stops_later_filters_and_subscription_handlers()
    {
        var firstFilterCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFilterInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PluginServer? server = null;
        server = PluginServer.Create();

        using (server)
        {
            server.Subscriptions.On<SubscriptionSignal>()
                .Where(_ =>
                {
                    server!.Dispose();
                    firstFilterCompleted.TrySetResult();
                    return true;
                })
                .Where(_ =>
                {
                    secondFilterInvoked.TrySetResult();
                    return true;
                })
                .RunLocal(_ => handlerInvoked.TrySetResult());

            server.Subscriptions.Publish(new SubscriptionSignal());

            await firstFilterCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var timeout = Task.Delay(TimeSpan.FromMilliseconds(200));
            var completed = await Task.WhenAny(secondFilterInvoked.Task, handlerInvoked.Task, timeout);

            Assert.Same(timeout, completed);
        }
    }

    private sealed record SubscriptionSignal;
}
