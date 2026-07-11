using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext>
{
    public HookPipeline<TEvent, TContext> Register<TResult>(
        Func<TEvent, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedResultChain<TResult>(HookLowering.RequiredPackage(irHandler, nameof(irHandler)), priority);
    }

    public HookPipeline<TEvent, TContext> Register<TResult>(
        Func<TEvent, TContext, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedResultChain<TResult>(HookLowering.RequiredPackage(irHandler, nameof(irHandler)), priority);
    }

    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }

    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, TResult> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(
            HookLowering.RequiredPackage(irHandler, nameof(irHandler)),
            handler,
            priority);
    }
}
