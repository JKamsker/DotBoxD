namespace DotBoxD.Plugins.Runtime.Hooks;

internal interface IResultHookRegistration<TEvent>
{
    int Priority { get; }
    long Order { get; }

    ValueTask<TResult?> InvokeAsync<TResult>(
        TEvent e,
        ResultHookDispatchOptions<TResult>? options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult;
}

internal sealed class ResultHookRegistration<TEvent, TContext>(
    HookPipeline<TEvent, TContext> owner,
    ResultHookSlot<TEvent, TContext>.Entry entry) : IResultHookRegistration<TEvent>
{
    public int Priority => entry.Priority;

    public long Order => entry.Order;

    public ValueTask<TResult?> InvokeAsync<TResult>(
        TEvent e,
        ResultHookDispatchOptions<TResult>? options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
        => owner.FireResultEntryAsync(entry, e, options, cancellationToken);
}
