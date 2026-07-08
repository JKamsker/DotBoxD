namespace DotBoxD.Plugins.Runtime.Hooks;

[PipelineSurface(PipelineTransport.Remote)]
public sealed partial class RemoteHookStage<TEvent, TCurrent, TContext>
{
    private readonly RemoteHookPipeline<TEvent, TContext> _root;

    internal RemoteHookStage(RemoteHookPipeline<TEvent, TContext> root)
        => _root = root;
    public RemoteHookStage<TEvent, TCurrent, TContext> Where(
        Func<TCurrent, TContext, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, TContext, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new RemoteHookStage<TEvent, TCurrent, TContext>(
            _root.AppendStep(irFilter, nameof(irFilter)));
    }
    public RemoteHookStage<TEvent, TCurrent, TContext> Where(
        Func<TCurrent, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new RemoteHookStage<TEvent, TCurrent, TContext>(
            _root.AppendStep(irFilter, nameof(irFilter)));
    }
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TContext, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(
            _root.AppendStep(irProjection, nameof(irProjection)));
    }
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(
            _root.AppendStep(irProjection, nameof(irProjection)));
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler)
        => _root.InstallLocal(package, handler);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value));
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }
    public RemoteHookPipeline<TEvent, TContext> Run(
        Func<TCurrent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChain(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public RemoteHookPipeline<TEvent, TContext> Run(
        Action<TCurrent, TContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, context) =>
        {
            handler(value, context);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> Run(
        Func<TCurrent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, _) => handler(value), irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> Run(
        Action<TCurrent> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Func<TCurrent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = RequiredKernel(irHandler);
        var package = _root.LocalTerminalPackage(kernel);
        return kernel.TryGetProjectedPayloadDecoder<TCurrent>(out var decoder)
            ? _root.InstallLocal(package, handler, decoder)
            : _root.InstallLocal(package, handler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Action<TCurrent, TContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, context) =>
        {
            handler(value, context);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Func<TCurrent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value), irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Action<TCurrent> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    private static IRKernel RequiredKernel(IRKernel? irHandler)
    {
        ArgumentNullException.ThrowIfNull(irHandler);
        return irHandler;
    }
}
