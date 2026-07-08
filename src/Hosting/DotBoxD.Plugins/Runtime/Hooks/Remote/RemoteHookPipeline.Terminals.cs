using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed partial class RemoteHookPipeline<TEvent>
{
    public RemoteHookPipeline<TEvent> Run(
        Func<TEvent, HookContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChain(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public RemoteHookPipeline<TEvent> Run(
        Action<TEvent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent> Run(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, _) => handler(e), irHandler);
    }

    public RemoteHookPipeline<TEvent> Run(
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

    public RemoteHookPipeline<TEvent> RunLocal(
        Func<TEvent, HookContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = RequiredKernel(irHandler);
        var package = LocalTerminalPackage(kernel);
        return kernel.TryGetProjectedPayloadDecoder<TEvent>(out var decoder)
            ? InstallLocal(package, handler, decoder)
            : InstallLocal(package, handler);
    }

    public RemoteHookPipeline<TEvent> RunLocal(
        Action<TEvent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent> RunLocal(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, _) => handler(e), irHandler);
    }

    public RemoteHookPipeline<TEvent> RunLocal(
        Action<TEvent> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    private static IRKernel RequiredKernel(IRKernel? irHandler)
    {
        ArgumentNullException.ThrowIfNull(irHandler);
        return irHandler;
    }
}
