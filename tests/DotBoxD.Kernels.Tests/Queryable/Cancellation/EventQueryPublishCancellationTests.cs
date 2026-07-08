using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
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

    [Fact]
    public async Task Sandbox_domain_caller_cancellation_is_not_swallowed_after_dispatch()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var invocations = 0;

        var handle = await host.Query<AttackTestEvent>()
            .Where(e => e.AttackerId == "player-1")
            .SubscribeAsync((_, _) =>
            {
                invocations++;
                cancellation.Cancel();
                throw new SandboxRuntimeException(
                    new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled"));
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var exception = await Record.ExceptionAsync(
            async () => await host.PublishAsync(new AttackTestEvent("player-1", "monster-1", 9, 1), context));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(1, invocations);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(1, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }
}
