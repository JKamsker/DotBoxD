using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Cancellation;

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

    [Fact]
    public async Task Sandbox_cancelled_filter_delivery_does_not_report_fault_or_continue()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var laterFilterInvoked = false;
        var handlerInvoked = false;
        var faultReported = false;

        Func<Ping, HookContext, ValueTask<bool>>[] filters =
        [
            (_, _) =>
            {
                cancellation.Cancel();
                throw new SandboxRuntimeException(
                    new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled"));
            },
            (_, _) =>
            {
                laterFilterInvoked = true;
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
            _ => faultReported = true);

        Assert.False(faultReported);
        Assert.False(laterFilterInvoked);
        Assert.False(handlerInvoked);
    }

    [Fact]
    public async Task Sandbox_cancelled_handler_delivery_does_not_report_fault_or_continue()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var laterHandlerInvoked = false;
        var faultReported = false;

        Func<Ping, HookContext, ValueTask<bool>>[] filters =
        [
            (_, _) => ValueTask.FromResult(true)
        ];
        Func<Ping, HookContext, HookContext, ValueTask>[] handlers =
        [
            (_, _, _) =>
            {
                cancellation.Cancel();
                throw new SandboxRuntimeException(
                    new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled"));
            },
            (_, _, _) =>
            {
                laterHandlerInvoked = true;
                return ValueTask.CompletedTask;
            }
        ];

        await SubscriptionDelivery.PublishSafelyAsync(
            filters,
            handlers,
            new Ping("probe"),
            context,
            context,
            _ => faultReported = true);

        Assert.False(faultReported);
        Assert.False(laterHandlerInvoked);
    }
}
