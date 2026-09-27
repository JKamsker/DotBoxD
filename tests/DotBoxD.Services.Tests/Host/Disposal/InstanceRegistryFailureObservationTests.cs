using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryFailureObservationTests
{
    public static IEnumerable<object[]> ReleaseCases()
    {
        foreach (var operation in new[] { "Release", "ReleaseAsync", "ReleaseAll", "ReleaseAllAsync", "Lease", "WaitingLease" })
        {
            yield return [operation, false];
            yield return [operation, true];
        }
    }

    [Theory]
    [MemberData(nameof(ReleaseCases))]
    public async Task Handled_disposal_failures_do_not_escape_through_an_unobserved_internal_task(string operation, bool asynchronous)
    {
        var marker = Guid.NewGuid().ToString("N");
        var unobserved = 0;
        var diagnostics = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, args) =>
        {
            if (args.Exception.InnerExceptions.Any(error => string.Equals(error.Message, marker, StringComparison.Ordinal)))
            {
                Interlocked.Increment(ref unobserved);
                args.SetObserved();
            }
        };
        EventHandler<RpcDiagnosticErrorEventArgs> onDiagnostic = (_, args) =>
        {
            if (string.Equals(args.Error.Message, marker, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref diagnostics);
            }
        };
        TaskScheduler.UnobservedTaskException += onUnobserved;
        RpcDiagnostics.Error += onDiagnostic;
        try
        {
            var error = ExerciseRelease(operation, asynchronous, marker);

            await AssertCollected(error);

            Assert.Equal(operation is "ReleaseAll" or "ReleaseAllAsync" ? 1 : 0, Volatile.Read(ref diagnostics));
            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally
        {
            RpcDiagnostics.Error -= onDiagnostic;
            TaskScheduler.UnobservedTaskException -= onUnobserved;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseRelease(string operation, bool asynchronous, string marker)
    {
        var registry = new InstanceRegistry();
        var error = new InvalidOperationException(marker);
        var calls = 0;
        void OnDispose() => Interlocked.Increment(ref calls);
        object instance = asynchronous ? new FailingAsyncDisposable(error, OnDispose) : new FailingDisposable(error, OnDispose);
        var id = registry.Register("service", instance);
        switch (operation)
        {
            case "Release":
                Assert.Same(error, Record.Exception(() => registry.Release("service", id)));
                break;
            case "ReleaseAsync":
                Assert.Same(error, Record.Exception(() => registry.ReleaseAsync("service", id).AsTask().GetAwaiter().GetResult()));
                break;
            case "ReleaseAll":
            case "ReleaseAllAsync":
                var survivor = new TrackingDisposable();
                registry.Register("service", survivor);
                if (operation == "ReleaseAll")
                {
                    registry.ReleaseAll();
                }
                else
                {
                    registry.ReleaseAllAsync().GetAwaiter().GetResult();
                }

                Assert.True(survivor.Disposed);
                break;
            case "Lease":
            case "WaitingLease":
                Assert.True(registry.TryAcquire("service", id, out _, out var lease));
                Task? waiting = null;
                if (operation == "WaitingLease")
                {
                    waiting = registry.ReleaseAsync("service", id).AsTask();
                    Assert.False(waiting.IsCompleted);
                }
                else
                {
                    registry.Release("service", id);
                }

                Assert.Equal(0, calls);
                Assert.Same(error, Record.Exception(() => lease.DisposeAsync().AsTask().GetAwaiter().GetResult()));
                if (waiting is not null)
                {
                    Assert.Same(error, Record.Exception(() => waiting.GetAwaiter().GetResult()));
                }

                lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Assert.Equal(1, calls);
        Assert.False(registry.TryGet("service", id, out _));
        return new WeakReference(error);
    }

    private static async Task AssertCollected(WeakReference error)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!error.IsAlive)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        // Prove the failing task and its exception became collectible before asserting that no
        // unobserved notification occurred. A task still rooted by the test would hide the defect.
        Assert.False(error.IsAlive, "The disposal exception must be collectible after its caller has handled it.");
    }

    private sealed class FailingDisposable(Exception error, Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
            throw error;
        }
    }

    private sealed class FailingAsyncDisposable(Exception error, Action onDispose) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            onDispose();
            await Task.Yield();
            throw error;
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
