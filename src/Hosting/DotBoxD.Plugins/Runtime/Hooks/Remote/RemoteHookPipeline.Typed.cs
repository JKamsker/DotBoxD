using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

[PipelineSurface(PipelineTransport.Remote)]
public sealed partial class RemoteHookPipeline<TEvent, TContext>
{
    private readonly RemoteHookPipeline<TEvent> _inner;
    private readonly Func<HookContext, TContext> _createContext;

    internal RemoteHookPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<HookContext, TContext> createContext,
        RemoteLocalHandlerRegistry? localHandlers = null)
        : this(new RemoteHookPipeline<TEvent>(install, localHandlers), createContext)
    {
    }

    private RemoteHookPipeline(
        RemoteHookPipeline<TEvent> inner,
        Func<HookContext, TContext> createContext)
        => (_inner, _createContext) = (inner, createContext);

    public RemoteHookPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        _inner.UseGeneratedChain(package);
        return this;
    }

    public RemoteHookPipeline<TEvent, TContext> Where(
        Func<TEvent, TContext, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TEvent, TContext, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return WithInner(_inner.AppendStep(irFilter, nameof(irFilter)));
    }
    public RemoteHookPipeline<TEvent, TContext> Where(
        Func<TEvent, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TEvent, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return WithInner(_inner.AppendStep(irFilter, nameof(irFilter)));
    }
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TEvent, TContext, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TEvent, TContext, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(
            WithInner(_inner.AppendStep(irProjection, nameof(irProjection))));
    }
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TEvent, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TEvent, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(
            WithInner(_inner.AppendStep(irProjection, nameof(irProjection))));
    }
    public RemoteHookPipeline<TEvent, TContext> Run(
        Func<TEvent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChain(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public RemoteHookPipeline<TEvent, TContext> Run(
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

    public RemoteHookPipeline<TEvent, TContext> Run(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, _) => handler(e), irHandler);
    }
    public RemoteHookPipeline<TEvent, TContext> Run(
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
    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Func<TEvent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = RequiredKernel(irHandler);
        var package = _inner.LocalTerminalPackage(kernel);
        return kernel.TryGetProjectedPayloadDecoder<TEvent>(out var decoder)
            ? InstallLocal(package, handler, decoder)
            : InstallLocal(package, handler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Action<TEvent, TContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, _) => handler(e), irHandler);
    }

    public RemoteHookPipeline<TEvent, TContext> RunLocal(
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

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler)
        => InstallLocal(package, handler);
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e));
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
        => InstallLocal(package, handler, decoder);
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
        => InstallLocal(package, handler, decoder);
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }
    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }
    internal RemoteHookPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InstallLocal<TProjected>(package, (value, rawContext) => handler(value, _createContext(rawContext)));
        return this;
    }

    internal RemoteHookPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler,
        Func<KernelRpcValue, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InstallLocal(package, (value, rawContext) => handler(value, _createContext(rawContext)), decoder);
        return this;
    }

    internal RemoteHookPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InstallLocal(package, (value, rawContext) => handler(value, _createContext(rawContext)), decoder);
        return this;
    }
    internal RemoteHookPipeline<TEvent, TContext> AppendStep<TInput, TOutput>(
        IRFunc<TInput, TOutput>? irFunc,
        string parameterName)
        => WithInner(_inner.AppendStep(irFunc, parameterName));
    internal RemoteHookPipeline<TEvent, TContext> AppendStep<TInput, TStageContext, TOutput>(
        IRFunc<TInput, TStageContext, TOutput>? irFunc,
        string parameterName)
        => WithInner(_inner.AppendStep(irFunc, parameterName));
    internal PluginPackage LocalTerminalPackage(IRKernel kernel)
        => _inner.LocalTerminalPackage(kernel);
    private RemoteHookPipeline<TEvent, TContext> WithInner(RemoteHookPipeline<TEvent> inner)
        => new(inner, _createContext);
    private static IRKernel RequiredKernel(IRKernel? irHandler)
    {
        ArgumentNullException.ThrowIfNull(irHandler);
        return irHandler;
    }
}
