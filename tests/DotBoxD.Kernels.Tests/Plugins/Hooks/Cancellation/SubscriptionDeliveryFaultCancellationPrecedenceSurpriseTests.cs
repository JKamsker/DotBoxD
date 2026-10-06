using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Cancellation;

public sealed class SubscriptionDeliveryFaultCancellationPrecedenceSurpriseTests
{
    [Fact]
    public async Task Handler_cancellation_stops_delivery_without_reporting_the_handler_fault()
    {
        var faultReported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var server = PluginServer.Create(onSubscriptionFault: fault => faultReported.TrySetResult(fault));

        server.Subscriptions.On<SubscriptionSignal>()
            .RunLocal((_, _) =>
            {
                cancellation.Cancel();
                handlerInvoked.TrySetResult();
                throw new InvalidOperationException("handler failure after caller cancellation");
            });

        server.Subscriptions.Publish(new SubscriptionSignal(), cancellation.Token);

        await handlerInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancellation.IsCancellationRequested);

        var timeout = Task.Delay(TimeSpan.FromMilliseconds(200));
        var completed = await Task.WhenAny(faultReported.Task, timeout);

        Assert.Same(timeout, completed);
    }

    [Fact]
    public async Task Live_token_handler_failure_is_still_reported()
    {
        var faultReported = new TaskCompletionSource<SubscriptionDeliveryFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = PluginServer.Create(onSubscriptionFault: fault => faultReported.TrySetResult(fault));

        server.Subscriptions.On<SubscriptionSignal>()
            .RunLocal(_ => throw new InvalidOperationException("handler failure"));

        server.Subscriptions.Publish(new SubscriptionSignal());

        var fault = await faultReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SubscriptionDeliveryStage.Handler, fault.Stage);
        Assert.IsType<InvalidOperationException>(fault.Exception);
    }

    private sealed record SubscriptionSignal;
}
