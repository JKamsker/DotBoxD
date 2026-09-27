using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledAwaitPumpLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inline_worker_restores_the_callers_context_on_success_or_failure(bool fail)
    {
        var previous = SynchronizationContext.Current;
        var result = CompiledAwaitPumpFixture.Result();
        var expected = new InvalidOperationException("ordinary callback failure");
        Func<SandboxExecutionResult> execute = () =>
        {
            Assert.NotNull(SynchronizationContext.Current);
            Assert.NotSame(previous, SynchronizationContext.Current);
            return fail ? throw expected : result;
        };

        if (fail)
        {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => CompiledAsyncWorker.RunInline(execute)));
        }
        else
        {
            Assert.Same(result, CompiledAsyncWorker.RunInline(execute));
        }
        Assert.Same(previous, SynchronizationContext.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public void Disposal_discards_pending_posts_and_rejects_new_posts(int count)
    {
        var invoked = 0;
        SynchronizationContext? context = null;
        var result = CompiledAwaitPumpFixture.Result();
        _ = CompiledAsyncWorker.RunInline(() =>
        {
            context = SynchronizationContext.Current!;
            for (var index = 0; index < count; index++)
            {
                context.Post(_ => invoked++, null);
            }
            return result;
        });

        Assert.Equal(0, invoked);
        ((IDisposable)context!).Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.Post(_ => invoked++, null));
        Assert.Equal(0, invoked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void Disposal_releases_waiting_senders_without_invoking_their_callbacks(int count)
    {
        var senders = new Thread[count];
        var errors = new Exception?[count];
        var invoked = 0;
        var result = CompiledAwaitPumpFixture.Result();
        _ = CompiledAsyncWorker.RunInline(() =>
        {
            var context = SynchronizationContext.Current!;
            for (var index = 0; index < count; index++)
            {
                var captured = index;
                senders[index] = new Thread(() =>
                {
                    try
                    {
                        context.Send(_ => Interlocked.Increment(ref invoked), null);
                    }
                    catch (Exception error)
                    {
                        errors[captured] = error;
                    }
                })
                { IsBackground = true };
                senders[index].Start();
                Assert.True(SpinWait.SpinUntil(
                    () => (senders[captured].ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5)));
            }
            return result;
        });

        for (var index = 0; index < count; index++)
        {
            Assert.True(senders[index].Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<ObjectDisposedException>(errors[index]);
        }
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void Active_pump_still_runs_queued_work_to_complete_a_binding()
    {
        var result = CompiledAwaitPumpFixture.Result();
        var expected = SandboxValue.FromInt32(42);
        _ = CompiledAsyncWorker.RunInline(() =>
        {
            var context = SynchronizationContext.Current!;
            var completion = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(_ => completion.SetResult(expected), null);
            var actual = ((ICompiledAwaitPump)context).RunToCompletion(new(completion.Task), CancellationToken.None);
            Assert.Same(expected, actual);
            return result;
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Retained_disposed_context_releases_queued_callback_state(int count)
    {
        var probe = CompiledAwaitPumpFixture.CreateDisposedQueue(count);
        await CompiledAwaitPumpFixture.AssertCollectedAsync(probe.References);
        GC.KeepAlive(probe.Context);
    }
}
