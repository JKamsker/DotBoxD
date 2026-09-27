namespace DotBoxD.Hosting.Execution.Compiled;

internal static class CompiledWorkFailureObserver
{
    internal static void Observe<T>(Lazy<Task<T>> work)
    {
        if (!work.IsValueCreated)
        {
            return;
        }

        var task = work.Value;
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        // Shared work continues after a waiter cancels, possibly with no remaining observer.
        _ = task.ContinueWith(
            static completed => { _ = completed.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
