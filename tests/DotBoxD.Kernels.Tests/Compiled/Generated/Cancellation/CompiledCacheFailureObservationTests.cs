using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Kernels.Tests.Compiled.Generated;

public sealed class CompiledCacheFailureObservationTests
{
    public static IEnumerable<object[]> AbandonedWorkCases()
    {
        foreach (var kind in new[] { "ArtifactDelegate", "ArtifactCompiler", "ExecutableDelegate", "ExecutableCache", "Materialized" })
        {
            yield return [kind, false];
            if (kind is not ("ArtifactDelegate" or "ArtifactCompiler"))
            {
                yield return [kind, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AbandonedWorkCases))]
    public async Task Late_failure_is_observed_after_the_only_waiter_cancels(string kind, bool disposeBeforeFailure)
    {
        var (plan, artifact) = await CompiledCacheCancellationFixture.CreateInputsAsync();
        var marker = Guid.NewGuid().ToString("N");
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception.InnerExceptions.Any(error => string.Equals(error.Message, marker, StringComparison.Ordinal)))
            {
                Interlocked.Increment(ref unobserved);
                args.SetObserved();
            }
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            var error = AbandonWork(kind, plan, artifact, marker, disposeBeforeFailure);
            await AssertCollectedAsync(error);
            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonWork(
        string kind, ExecutionPlan plan, CompiledArtifact artifact, string marker, bool disposeBeforeFailure)
    {
        using var fixture = new CompiledCacheCancellationFixture(kind, plan, artifact);
        using var cancellation = new CancellationTokenSource();
        var waiter = fixture.RequestAsync(cancellation.Token);
        Assert.False(waiter.IsCompleted);
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => waiter.GetAwaiter().GetResult());
        Assert.False(fixture.Work.IsCompleted);
        Assert.Equal(1, fixture.Calls);
        if (disposeBeforeFailure)
        {
            fixture.Dispose();
        }

        var error = new InvalidOperationException(marker);
        fixture.Fail(error);
        return new WeakReference(error);
    }

    private static async Task AssertCollectedAsync(WeakReference error)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            // Check only after draining this collection's finalizers, which raise the event.
            if (!error.IsAlive)
            {
                return;
            }
            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(error.IsAlive, "The failed work must be collectible before checking unobserved notifications.");
    }
}
