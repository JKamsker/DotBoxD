using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Authoring;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class EventQueryRoutingBufferTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(32, "complete")]
    [InlineData(32, "unreadable")]
    [InlineData(32, "cancel")]
    [InlineData(2 * 1024 * 1024, "complete")]
    [InlineData(2 * 1024 * 1024, "unreadable")]
    [InlineData(2 * 1024 * 1024, "cancel")]
    public Task Publishing_transient_keys_does_not_retain_their_large_buffers(int length, string outcome) =>
        RunOnNewThread(() =>
        {
            var host = new EventQueryHost();
            var calls = 0;
            using var subscription = host.Query<Sample>().Where(e => e.A == "selected" && e.Z == "selected")
                .SubscribeAsync((_, _) =>
                {
                    calls++;
                    return ValueTask.CompletedTask;
                }).GetAwaiter().GetResult();
            for (var i = 0; i < 20; i++)
            {
                PublishTransient(host, 32, outcome);
            }

            var before = GC.GetTotalMemory(forceFullCollection: true);
            PublishTransient(host, length, outcome);
            var retained = GC.GetTotalMemory(forceFullCollection: true) - before;

            output.WriteLine($"{length} characters, {outcome}: {retained} retained bytes.");
            Assert.Equal(0, subscription.FilterEvaluations);
            Assert.Equal(0, calls);
            // Allow unrelated runtime bookkeeping, but not the multi-megabyte transient key.
            Assert.True(retained < 256 * 1024, $"Routing retained {retained} bytes after publishing a transient key.");

            host.PublishAsync(new Sample("selected"), NewContext(CancellationToken.None)).GetAwaiter().GetResult();
            Assert.Equal(1, calls);
            Assert.Equal(1, subscription.Dispatches);
            GC.KeepAlive(host);
        });

    [Fact]
    public async Task Large_keys_still_match_and_dispatch()
    {
        var host = new EventQueryHost();
        var selected = new string('x', 4096);
        var calls = 0;
        using var subscription = await host.Query<Sample>().Where(e => e.A == selected && e.Z == "selected")
            .SubscribeAsync((_, _) =>
            {
                calls++;
                return ValueTask.CompletedTask;
            });

        await host.PublishAsync(new Sample(selected), NewContext(CancellationToken.None));
        await host.PublishAsync(new Sample(selected), NewContext(CancellationToken.None));

        Assert.Equal(2, calls);
        Assert.Equal(2, subscription.Dispatches);
    }

    [Fact]
    public Task Small_keys_keep_the_existing_warm_allocation_budget() => RunOnNewThread(() =>
    {
        var host = new EventQueryHost();
        using var subscription = host.Query<Sample>().Where(e => e.A == "selected" && e.Z == "selected")
            .SubscribeAsync((_, _) => ValueTask.CompletedTask).GetAwaiter().GetResult();
        var value = new Sample("other");
        var context = NewContext(CancellationToken.None);
        _ = Measure(host, value, context);

        var bytes = Measure(host, value, context);

        output.WriteLine($"Small composite key: {bytes / 1000D} B/publish.");
        Assert.Equal(0, subscription.FilterEvaluations);
        Assert.True(bytes <= 280_000, $"Small-key routing allocated {bytes} bytes for 1,000 publishes.");
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PublishTransient(EventQueryHost host, int length, string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        var value = new Sample(new string('x', length), outcome, cancellation);
        var pending = host.PublishAsync(value, NewContext(cancellation.Token));
        Assert.True(pending.IsCompleted);
        if (outcome == "cancel")
        {
            var error = Assert.ThrowsAny<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            pending.GetAwaiter().GetResult();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(EventQueryHost host, Sample value, HookContext context)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            host.PublishAsync(value, context).GetAwaiter().GetResult();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Task RunOnNewThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        { IsBackground = true }.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static HookContext NewContext(CancellationToken cancellationToken) =>
        new(new InMemoryPluginMessageSink(), cancellationToken);

    private sealed class Sample(string a, string outcome = "complete", CancellationTokenSource? cancellation = null)
    {
        public string A => a;
        public string Z
        {
            get
            {
                if (outcome == "unreadable")
                {
                    throw new InvalidOperationException("Unavailable member.");
                }

                if (outcome == "cancel")
                {
                    cancellation!.Cancel();
                }

                return "selected";
            }
        }
    }
}
