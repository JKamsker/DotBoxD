namespace DotBoxD.Plugins.Runtime.Hooks;

public sealed partial class RemoteHookStage<TEvent, TCurrent>
{
    public RemoteHookPipeline<TEvent> UseGeneratedResultChain<TResult>(PluginPackage package, int priority = 0)
        where TResult : struct, IHookResult
        => _root.UseGeneratedResultChain<TResult>(package, priority);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            e => handler((TCurrent)(object)e!),
            priority);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context) => handler((TCurrent)(object)e!, context),
            priority);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            e => handler((TCurrent)(object)e!),
            priority);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, HookContext, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context) => handler((TCurrent)(object)e!, context),
            priority);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, ct) => handler((TCurrent)(object)e!, ct),
            priority);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TCurrent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, context, ct) => handler((TCurrent)(object)e!, context, ct),
            priority);
    }

    public RemoteHookPipeline<TEvent> Register<TResult>(
        Func<TCurrent, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedResultChain<TResult>(HookLowering.RequiredPackage(irHandler, nameof(irHandler)), priority);
    }

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TCurrent, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TCurrent, HookContext, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TCurrent, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TCurrent, HookContext, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TCurrent, CancellationToken, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public RemoteHookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TCurrent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }
}
