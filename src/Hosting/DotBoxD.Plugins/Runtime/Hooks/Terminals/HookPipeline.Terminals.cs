using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext>
{
    /// <summary>Native host terminal — runs in-process (NOT sandboxed). Use sparingly.</summary>
    public HookPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    /// <summary>
    /// The terminal the analyzer lowers to verified IR. It never runs as host code: un-lowered it
    /// throws, so plugin logic cannot accidentally execute unsandboxed.
    /// </summary>
    public HookPipeline<TEvent, TContext> Run(
        Func<TEvent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChain(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public HookPipeline<TEvent, TContext> Run(
        Action<TEvent, TContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public HookPipeline<TEvent, TContext> Run(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, _) => handler(e), irHandler);
    }

    public HookPipeline<TEvent, TContext> Run(
        Action<TEvent> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, irHandler);
    }
}
