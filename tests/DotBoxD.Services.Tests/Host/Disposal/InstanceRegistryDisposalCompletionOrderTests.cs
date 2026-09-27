using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryDisposalCompletionOrderTests
{
    public static IEnumerable<object[]> CompletionCases()
    {
        foreach (var asynchronousOwner in new[] { false, true })
            foreach (var asynchronousInstance in new[] { false, true })
                foreach (var fail in new[] { false, true })
                    foreach (var reportFailure in new[] { false, true })
                    {
                        yield return [asynchronousOwner, asynchronousInstance, fail, reportFailure];
                    }
    }

    [Theory]
    [MemberData(nameof(CompletionCases))]
    public async Task Waiters_complete_after_registry_cleanup(
        bool asynchronousOwner, bool asynchronousInstance, bool fail, bool reportFailure)
    {
        var expected = fail ? new InvalidOperationException("Disposal failed.") : null;
        var state = new DisposalState(expected);
        object instance = asynchronousInstance ? new AsyncInstance(state) : new SyncInstance(state);
        var disposal = new InstanceRegistryDisposal(instance);
        var cleanupCalls = 0;
        var completedDuringCleanup = false;
        var diagnostics = 0;
        EventHandler<RpcDiagnosticErrorEventArgs> onDiagnostic = (_, args) =>
        {
            if (ReferenceEquals(args.Error, expected))
            {
                Interlocked.Increment(ref diagnostics);
            }
        };
        RpcDiagnostics.Error += onDiagnostic;
        try
        {
            void OnCompleted(object completed)
            {
                Assert.Same(instance, completed);
                cleanupCalls++;
                completedDuringCleanup = disposal.Completion.Task.IsCompleted;
            }

            var ownerError = asynchronousOwner
                ? await Record.ExceptionAsync(() => InstanceRegistryDisposer.DisposeAndCompleteAsync(disposal, OnCompleted, reportFailure))
                : Record.Exception(() => InstanceRegistryDisposer.DisposeAndComplete(disposal, OnCompleted, reportFailure));
            var waiterError = await Record.ExceptionAsync(() => disposal.Completion.Task);

            Assert.Same(reportFailure ? null : expected, ownerError);
            Assert.Same(expected, waiterError);
            Assert.Equal(fail && reportFailure ? 1 : 0, diagnostics);
            Assert.Equal(1, state.Calls);
            Assert.Equal(1, cleanupCalls);
            Assert.False(completedDuringCleanup, "Release waiters must remain pending until registry cleanup finishes.");
        }
        finally
        {
            RpcDiagnostics.Error -= onDiagnostic;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Awaited_leased_release_allows_registration_after_disposal(bool asynchronous, bool fail)
    {
        for (var iteration = 0; iteration < 256; iteration++)
        {
            var registry = new InstanceRegistry();
            var expected = fail ? new InvalidOperationException("Disposal failed.") : null;
            var state = new DisposalState(expected);
            object instance = asynchronous ? new AsyncInstance(state) : new SyncInstance(state);
            var id = registry.Register("service", instance);
            Assert.True(registry.TryAcquire("service", id, out _, out var lease));
            var release = registry.ReleaseAsync("service", id).AsTask();
            var registration = RegisterAfterRelease(release, registry, instance, expected);
            var returning = Task.Run(() => Record.ExceptionAsync(() => lease.DisposeAsync().AsTask()));

            var registered = await registration;
            Assert.Same(expected, await returning);
            Assert.True(registry.TryGet("again", registered, out var found));
            Assert.Same(instance, found);
            Assert.Equal(1, state.Calls);
            // The second registration owns another lifetime, including another disposal attempt.
            var finalError = await Record.ExceptionAsync(() => registry.ReleaseAsync("again", registered).AsTask());
            Assert.Same(expected, finalError);
            Assert.Equal(2, state.Calls);
        }
    }

    private static async Task<string> RegisterAfterRelease(Task release, InstanceRegistry registry, object instance, Exception? expected)
    {
        Exception? failure = null;
        try
        {
            await release.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;
        }

        Assert.Same(expected, failure);
        return registry.Register("again", instance);
    }

    private sealed class DisposalState(Exception? error)
    {
        public int Calls;
        public void Dispose()
        {
            Interlocked.Increment(ref Calls);
            if (error is not null)
            {
                throw error;
            }
        }
    }

    private sealed class SyncInstance(DisposalState state) : IDisposable
    {
        public void Dispose() => state.Dispose();
    }

    private sealed class AsyncInstance(DisposalState state) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            state.Dispose();
        }
    }
}
