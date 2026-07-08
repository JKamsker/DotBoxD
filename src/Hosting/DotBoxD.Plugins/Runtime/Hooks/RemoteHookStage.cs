namespace DotBoxD.Plugins.Runtime.Hooks;

[PipelineSurface(PipelineTransport.Remote)]
public sealed partial class RemoteHookStage<TEvent, TCurrent>
{
    private readonly RemoteHookPipeline<TEvent> _root;

    internal RemoteHookStage(RemoteHookPipeline<TEvent> root)
        => _root = root;
    public RemoteHookStage<TEvent, TCurrent> Where(
        Func<TCurrent, HookContext, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, HookContext, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new RemoteHookStage<TEvent, TCurrent>(
            _root.AppendStep(irFilter, nameof(irFilter)));
    }
    public RemoteHookStage<TEvent, TCurrent> Where(
        Func<TCurrent, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new RemoteHookStage<TEvent, TCurrent>(
            _root.AppendStep(irFilter, nameof(irFilter)));
    }
    public RemoteHookStage<TEvent, TNext> Select<TNext>(
        Func<TCurrent, HookContext, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, HookContext, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(
            _root.AppendStep(irProjection, nameof(irProjection)));
    }
    public RemoteHookStage<TEvent, TNext> Select<TNext>(
        Func<TCurrent, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(
            _root.AppendStep(irProjection, nameof(irProjection)));
    }

    public RemoteHookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    /// <summary>
    /// Installs a lowered <c>RunLocal</c> chain whose projected type is <typeparamref name="TCurrent"/> (the
    /// type produced by the preceding <c>Select</c>). The lowered filter+projection installs server-side and
    /// the native delegate is registered to receive the projected value pushed back per matching event.
    /// </summary>
    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, HookContext, ValueTask> handler)
        => _root.InstallLocal(package, handler);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value));
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    // Decoder overloads: a projection RunLocal chain whose projected type TCurrent is wire-eligible installs
    // with the generated reflection-free decoder, emitted by the interceptor as the 3rd argument.
    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, HookContext, ValueTask> handler, Func<KernelRpcValue, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent, HookContext> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, ValueTask> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent, HookContext> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, ValueTask> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }
    public RemoteHookPipeline<TEvent> Run(
        Func<TCurrent, HookContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChain(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public RemoteHookPipeline<TEvent> Run(
        Action<TCurrent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent> Run(
        Func<TCurrent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, _) => handler(value), irHandler);
    }

    public RemoteHookPipeline<TEvent> Run(
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

    public RemoteHookPipeline<TEvent> RunLocal(
        Func<TCurrent, HookContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = RequiredKernel(irHandler);
        var package = _root.LocalTerminalPackage(kernel);
        return kernel.TryGetProjectedPayloadDecoder<TCurrent>(out var decoder)
            ? _root.InstallLocal(package, handler, decoder)
            : _root.InstallLocal(package, handler);
    }

    public RemoteHookPipeline<TEvent> RunLocal(
        Action<TCurrent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent> RunLocal(
        Func<TCurrent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value), irHandler);
    }

    public RemoteHookPipeline<TEvent> RunLocal(
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
