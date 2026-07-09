namespace DotBoxD.Plugins.Runtime;

public sealed partial class RemoteHookPipeline<TEvent, TContext>
{
    public RemoteHookPipeline<TEvent, TContext> Register<TResult>(
        Func<TEvent, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedResultChain<TResult>(Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)), priority);
    }

    public RemoteHookPipeline<TEvent, TContext> Register<TResult>(
        Func<TEvent, TContext, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedResultChain<TResult>(Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)), priority);
    }

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, CancellationToken, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, CancellationToken, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            Hooks.HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedResultChain<TResult>(
        PluginPackage package,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        _inner.UseGeneratedResultChain<TResult>(package, priority);
        return this;
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(package, (e, _) => handler(e), priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(
            package,
            (e, context, _) => new ValueTask<TResult>(handler(e, context)),
            priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, (e, _, _) => handler(e), priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, (e, context, _) => handler(e, context), priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, (e, _, ct) => handler(e, ct), priority);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChainCore<TResult>(package, handler, priority);
    }

    private RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalResultChainCore<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.UseGeneratedLocalResultChainCore<TResult>(
            package,
            (e, rawContext, ct) => handler(e, _createContext(rawContext), ct),
            priority);
        return this;
    }

}
