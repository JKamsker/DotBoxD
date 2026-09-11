using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class SubscriptionFaultObserverDisposalSurpriseTests
{
    [Fact]
    public async Task Fault_observer_disposal_stops_the_remaining_subscription_handlers()
    {
        var faultObserved = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PluginServer? server = null;
        server = PluginServer.Create(onSubscriptionFault: fault =>
        {
            server!.Dispose();
            faultObserved.TrySetResult(fault);
        });

        using (server)
        {
            server.Subscriptions.On<SubscriptionSignal>()
                .RunLocal(_ => throw new InvalidOperationException("handler failure"))
                .RunLocal(_ => fallbackInvoked.TrySetResult());

            server.Subscriptions.Publish(new SubscriptionSignal());

            var fault = await faultObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(SubscriptionDeliveryStage.Handler, fault.Stage);
            var completed = await Task.WhenAny(fallbackInvoked.Task, Task.Delay(TimeSpan.FromMilliseconds(200)));

            Assert.NotSame(fallbackInvoked.Task, completed);
        }
    }

    private sealed record SubscriptionSignal;
}
