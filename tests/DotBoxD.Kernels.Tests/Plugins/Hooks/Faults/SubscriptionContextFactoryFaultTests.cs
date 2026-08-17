using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class SubscriptionContextFactoryFaultTests
{
    [Fact]
    public async Task Publish_isolated_context_factory_fault_does_not_escape_after_earlier_delivery()
    {
        var delivered = 0;
        var healthyDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportedFault = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = PluginServer.Create(
            onSubscriptionFault: fault => reportedFault.TrySetResult(fault));

        server.Subscriptions.On<SubscriptionSignal>()
            .RunLocal(_ =>
            {
                Interlocked.Increment(ref delivered);
                healthyDelivery.TrySetResult();
            });
        server.Subscriptions.On<SubscriptionSignal, FaultingSubscriptionContext>(
                _ => throw new InvalidOperationException("context factory failure"))
            .RunLocal((_, _) => { });

        var exception = Record.Exception(
            () => server.Subscriptions.Publish(new SubscriptionSignal()));

        Assert.Null(exception);
        await healthyDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var fault = await reportedFault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(typeof(SubscriptionSignal), fault.EventType);
        Assert.Equal("context factory failure", Assert.IsType<InvalidOperationException>(fault.Exception).Message);
        Assert.Equal(1, Volatile.Read(ref delivered));
    }

    private sealed record SubscriptionSignal;

    private sealed record FaultingSubscriptionContext(HookContext Raw);
}
