namespace DotBoxD.Services.Transport;

internal static class TransportTaskWaiter
{
    public static Task WaitAsync(Task task, CancellationToken ct)
    {
        if (!ct.CanBeCanceled || task.IsCompleted)
        {
            return task;
        }

        return WaitCoreAsync(task, ct);
    }

    private static async Task WaitCoreAsync(Task task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), canceled))
        {
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
            {
                throw new OperationCanceledException(ct);
            }
        }

        await task.ConfigureAwait(false);
    }
}
