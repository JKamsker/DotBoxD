using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQuerySubscriptionDisposalTests
{
    [Fact]
    public async Task Disposing_a_snapshotted_subscription_stops_its_handler_before_it_can_start()
    {
        var host = new EventQueryHost();
        var firstHandlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandlerCalls = 0;

        await host.Query<AttackTestEvent>()
            .SubscribeAsync(async (_, _) =>
            {
                firstHandlerStarted.SetResult();
                await releaseFirstHandler.Task;
            });

        var secondHandle = await host.Query<AttackTestEvent>()
            .SubscribeAsync((_, _) =>
            {
                secondHandlerCalls++;
                return ValueTask.CompletedTask;
            });

        var publish = host.PublishAsync(new AttackTestEvent("a", "b", 1, 1), NewContext()).AsTask();
        await firstHandlerStarted.Task;

        secondHandle.Dispose();
        releaseFirstHandler.SetResult();
        await publish;

        Assert.Equal(0, secondHandlerCalls);
        Assert.Equal(0, secondHandle.Dispatches);
    }

    private static HookContext NewContext() => new(new InMemoryPluginMessageSink(), CancellationToken.None);
}
