using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Authoring;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class EventQueryHandleRetentionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Retained_disposed_handles_release_callbacks_and_preserve_diagnostics(bool projection, bool compiled)
    {
        var host = new EventQueryHost();
        var (handle, reference) = Register(host, projection, compiled);
        var published = compiled ? 20 : 1;
        Collect();
        Assert.True(reference.IsAlive, "An active subscription must retain its callbacks.");
        Assert.Equal(compiled, handle.IsCompiled);
        Assert.Equal(published, handle.Dispatches);

        handle.Dispose();
        await AssertCollected(reference);

        using var other = await host.Query<Sample>().SubscribeAsync(static (_, _) => ValueTask.CompletedTask);
        await host.PublishAsync(new Sample(3), Context());
        Assert.Equal(compiled, handle.IsCompiled);
        Assert.Equal(published + 1, handle.EventsObserved);
        Assert.Equal(published, handle.FilterEvaluations);
        Assert.Equal(published, handle.Matches);
        Assert.Equal(published, handle.Dispatches);
        Assert.Equal(1, other.Dispatches);
        Assert.NotEmpty(handle.Describe());
        handle.Dispose();
        GC.KeepAlive(handle);
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discarded_disposed_handles_also_release_callbacks(bool projection)
    {
        var host = new EventQueryHost();
        var reference = RegisterAndDiscard(host, projection);

        await AssertCollected(reference);

        Assert.False(host.HasSubscriptions<Sample>());
        GC.KeepAlive(host);
    }

    [Fact]
    public async Task A_running_handler_remains_alive_until_completion_after_disposal()
    {
        var host = new EventQueryHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (handle, reference, publish) = StartBlocked(host, started, release);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handle.Dispose();
        Collect();
        Assert.True(reference.IsAlive);

        release.SetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertCollected(reference);

        Assert.Equal(1, handle.Dispatches);
        Assert.Equal(1, handle.Matches);
        Assert.False(handle.IsCompiled);
        GC.KeepAlive(publish);
        GC.KeepAlive(handle);
        GC.KeepAlive(host);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (EventQuerySubscriptionHandle Handle, WeakReference Reference) Register(EventQueryHost host, bool projection, bool compiled)
    {
        var capture = new Capture();
        var handle = projection
            ? host.Query<Sample>().Select(e => new Notice(e.Value, capture.Constant))
                .SubscribeAsync(static (_, _) => ValueTask.CompletedTask).GetAwaiter().GetResult()
            : host.Query<Sample>().SubscribeAsync(capture.Handle).GetAwaiter().GetResult();
        var context = Context();
        for (var index = 0; index < (compiled ? 20 : 1); index++)
        {
            host.PublishAsync(new Sample(index), context).GetAwaiter().GetResult();
        }

        return (handle, new WeakReference(capture));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterAndDiscard(EventQueryHost host, bool projection)
    {
        var (handle, reference) = Register(host, projection, compiled: true);
        handle.Dispose();
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (EventQuerySubscriptionHandle Handle, WeakReference Reference, Task Publish) StartBlocked(
        EventQueryHost host, TaskCompletionSource started, TaskCompletionSource release)
    {
        var capture = new BlockingCapture(started, release);
        var handle = host.Query<Sample>().SubscribeAsync(capture.Handle).GetAwaiter().GetResult();
        var publish = host.PublishAsync(new Sample(1), Context()).AsTask();
        return (handle, new WeakReference(capture), publish);
    }

    private static async Task AssertCollected(WeakReference reference)
    {
        var timer = Stopwatch.StartNew();
        while (reference.IsAlive && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Collect();
            if (reference.IsAlive)
            {
                await Task.Delay(10);
            }
        }

        Assert.False(reference.IsAlive, "A disposed query handle must release completed callback targets.");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private sealed record Sample(int Value);
    private sealed record Notice(int Value, int Constant);

    private sealed class Capture
    {
        public int Constant => 7;
        public ValueTask Handle(Sample value, HookContext context) => ValueTask.CompletedTask;
    }

    private sealed class BlockingCapture(TaskCompletionSource started, TaskCompletionSource release)
    {
        public async ValueTask Handle(Sample value, HookContext context)
        {
            started.SetResult();
            await release.Task;
            GC.KeepAlive(this);
        }
    }
}
