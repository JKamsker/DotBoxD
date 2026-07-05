namespace DotBoxD.Plugins.Runtime.Hooks;

internal sealed class ResultHookEntryInvoker<TEvent, TContext>(Action<ResultHookFault>? onFault)
{
    public async ValueTask<IHookResult?> InvokeRemoteAsync<TResult>(
        ResultHookSlot<TEvent, TContext>.Entry entry,
        TEvent e,
        HookContext rawContext,
        TContext context,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        if (options.RemoteHandlerTimeout == Timeout.InfiniteTimeSpan)
        {
            var pending = entry.Invoke(e, rawContext, context, cancellationToken);
            return pending.IsCompletedSuccessfully
                ? pending.Result
                : await pending.ConfigureAwait(false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.RemoteHandlerTimeout);
        try
        {
            var pending = entry.Invoke(e, rawContext, context, timeoutCts.Token);
            if (pending.IsCompletedSuccessfully)
            {
                return pending.Result;
            }

            return await pending.AsTask()
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            Report(new TimeoutException(
                $"Remote result hook for '{typeof(TEvent).Name}' timed out after {options.RemoteHandlerTimeout}."));
            return options.RemoteTimeoutResult is { } result ? result : null;
        }
    }

    public void Report(Exception exception)
    {
        if (onFault is null)
        {
            return;
        }

        try
        {
            onFault(new ResultHookFault(typeof(TEvent), exception));
        }
        catch
        {
            // A faulty fault observer must never escalate into or abort dispatch.
        }
    }
}
