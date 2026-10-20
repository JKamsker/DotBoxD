using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QuerySnapshotSharingTests
{
    [Fact]
    public async Task Registration_during_publish_does_not_mutate_the_active_bucket()
    {
        var host = new EventQueryHost();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        using var first = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync(async (_, _) =>
        {
            calls.Add("first");
            entered.TrySetResult();
            await resume.Task;
        });
        using var second = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync((_, _) =>
        {
            calls.Add("second");
            return ValueTask.CompletedTask;
        });
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        var publish = host.PublishAsync(new Event(7, 7), context).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var third = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync((_, _) =>
        {
            calls.Add("third");
            return ValueTask.CompletedTask;
        });
        resume.TrySetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "first", "second" }, calls);

        calls.Clear();
        await host.PublishAsync(new Event(7, 7), context);
        Assert.Equal(new[] { "first", "second", "third" }, calls);
    }

    [Fact]
    public async Task Removal_preserves_broad_bucket_and_remaining_group_order()
    {
        var host = new EventQueryHost();
        var calls = new List<string>();
        using var firstA = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync(Handler("A1"));
        using var firstB = await host.Query<Event>().Where(e => e.B == 7).SubscribeAsync(Handler("B"));
        using var secondA = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync(Handler("A2"));
        using var broad = await host.Query<Event>().Where(e => e.A > 0).SubscribeAsync(Handler("broad"));
        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        await host.PublishAsync(new Event(7, 7), context);
        Assert.Equal(new[] { "broad", "A1", "A2", "B" }, calls);

        firstA.Dispose();
        calls.Clear();
        await host.PublishAsync(new Event(7, 7), context);
        Assert.Equal(new[] { "broad", "B", "A2" }, calls);

        secondA.Dispose();
        calls.Clear();
        await host.PublishAsync(new Event(7, 7), context);
        Assert.Equal(new[] { "broad", "B" }, calls);
        broad.Dispose();
        firstB.Dispose();
        Assert.False(host.HasSubscriptions<Event>());

        Func<Event, HookContext, ValueTask> Handler(string name) => (_, _) =>
        {
            calls.Add(name);
            return ValueTask.CompletedTask;
        };
    }

    [Fact]
    public async Task Removing_a_sibling_bucket_keeps_in_progress_candidates_intact()
    {
        var host = new EventQueryHost();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var matches = 0;
        using var first = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync(async (_, _) =>
        {
            entered.TrySetResult();
            await resume.Task;
        });
        using var other = await host.Query<Event>().Where(e => e.A == 8).SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
        using var last = await host.Query<Event>().Where(e => e.A == 7).SubscribeAsync((_, _) =>
        {
            matches++;
            return ValueTask.CompletedTask;
        });
        var publish = host.PublishAsync(new Event(7, 7), new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        other.Dispose();
        resume.TrySetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, matches);
    }

    private sealed record Event(int A, int B);
}
