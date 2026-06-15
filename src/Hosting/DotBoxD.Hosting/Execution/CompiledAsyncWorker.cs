using System.Runtime.ExceptionServices;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

internal sealed class CompiledAsyncWorker(Func<SandboxExecutionResult> execute)
{
    private readonly TaskCompletionSource<SandboxExecutionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static ValueTask<SandboxExecutionResult> RunAsync(Func<SandboxExecutionResult> execute)
    {
        var worker = new CompiledAsyncWorker(execute);
        worker.Start();
        return new ValueTask<SandboxExecutionResult>(worker._completion.Task);
    }

    private void Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DotBoxD compiled async worker"
        };
        thread.Start();
    }

    private void Run()
    {
        using var pump = new CompiledAwaitPump();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(pump);
        using var scope = CompiledBindingDispatcher.InstallAwaitPump(pump);
        try
        {
            _completion.SetResult(execute());
        }
        catch (Exception ex)
        {
            _completion.SetException(ex);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class CompiledAwaitPump : SynchronizationContext, ICompiledAwaitPump, IDisposable
    {
        private readonly Queue<WorkItem> _queue = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private bool _disposed;

        public SandboxValue RunToCompletion(ValueTask<SandboxValue> pending)
        {
            var task = pending.AsTask();
            if (!task.IsCompleted)
            {
                _ = task.ContinueWith(
                    static (_, state) => ((CompiledAwaitPump)state!).Signal(),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            while (!task.IsCompleted)
            {
                if (TryDequeue(out var item))
                {
                    Invoke(item);
                    continue;
                }

                _signal.WaitOne();
            }

            return task.GetAwaiter().GetResult();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            lock (_queue)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _queue.Enqueue(new WorkItem(d, state));
            }

            Signal();
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (Environment.CurrentManagedThreadId == _ownerThreadId)
            {
                d(state);
                return;
            }

            using var completed = new ManualResetEventSlim();
            ExceptionDispatchInfo? error = null;
            Post(s =>
            {
                try
                {
                    d(s);
                }
                catch (Exception ex)
                {
                    error = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    completed.Set();
                }
            }, state);
            completed.Wait();
            error?.Throw();
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Dispose()
        {
            lock (_queue)
            {
                _disposed = true;
                _queue.Clear();
            }

            _signal.Dispose();
        }

        private void Signal() => _signal.Set();

        private bool TryDequeue(out WorkItem item)
        {
            lock (_queue)
            {
                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                    return true;
                }
            }

            item = default;
            return false;
        }

        private static void Invoke(WorkItem item) => item.Callback(item.State);

        private readonly record struct WorkItem(SendOrPostCallback Callback, object? State);
    }
}
