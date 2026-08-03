using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryFilterFaultCancellationTests
{
    [Fact]
    public async Task Filter_getter_fault_does_not_swallow_caller_cancellation()
    {
        var host = new EventQueryHost();
        using var cancellation = new CancellationTokenSource();
        var handlerInvoked = false;

        var handle = await host.Query<FilterFaultCancelEvent>()
            .Where(e => e.Damage > 10)
            .SubscribeAsync((_, _) =>
            {
                handlerInvoked = true;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);
        var exception = await Record.ExceptionAsync(
            async () => await host.PublishAsync(new FilterFaultCancelEvent(cancellation), context));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(handlerInvoked);
        Assert.Equal(1, handle.FilterEvaluations);
        Assert.Equal(0, handle.Matches);
        Assert.Equal(0, handle.Dispatches);
    }

    private sealed class FilterFaultCancelEvent(CancellationTokenSource cancellation)
    {
        public int Damage
        {
            get
            {
                cancellation.Cancel();
                throw new InvalidOperationException("filter failed");
            }
        }
    }
}
