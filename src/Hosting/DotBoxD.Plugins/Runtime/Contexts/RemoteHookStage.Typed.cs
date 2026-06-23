namespace DotBoxD.Plugins.Runtime.Hooks;

public sealed class RemoteHookStage<TEvent, TCurrent, TContext>
{
    private readonly RemoteHookStage<TEvent, TCurrent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal RemoteHookStage(
        RemoteHookStage<TEvent, TCurrent> inner,
        Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public RemoteHookStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _inner.Where((value, ctx) => filter(value, _contextFactory(ctx)));
        return this;
    }

    public RemoteHookStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
    {
        _inner.Where(filter);
        return this;
    }

    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(
            _inner.Select((value, ctx) => projection(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TNext> projection)
        => new(_inner.Select(projection), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => new(_inner.UseGeneratedChain(package), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedResultChain<TResult>(
        PluginPackage package,
        int priority = 0)
        where TResult : struct, IHookResult
        => new(_inner.UseGeneratedResultChain<TResult>(package, priority), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new RemoteHookPipeline<TEvent, TContext>(
            _inner.UseGeneratedLocalChain(package, (value, ctx) => handler(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalChain(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler)
        => new(_inner.UseGeneratedLocalChain(package, handler), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler)
        => new(_inner.UseGeneratedLocalChain(package, handler), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new RemoteHookPipeline<TEvent, TContext>(
            _inner.UseGeneratedLocalChain(package, (value, ctx) => handler(value, _contextFactory(ctx)), decoder),
            _contextFactory);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalChain(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new RemoteHookPipeline<TEvent, TContext>(
            _inner.UseGeneratedLocalChain(package, (value, ctx) => handler(value, _contextFactory(ctx)), decoder),
            _contextFactory);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalChain(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteHookPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent, TContext> Register<TResult>(
        Func<TCurrent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultNotLowered();

    public RemoteHookPipeline<TEvent, TContext> Register<TResult>(Func<TCurrent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultNotLowered();

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TCurrent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw ResultLocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote hook Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static InvalidOperationException ResultNotLowered()
        => new("Remote hook Register(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote hook RunLocal requires an event callback transport; use PluginServer.Hooks for local handlers.");

    private static NotSupportedException ResultLocalHandlersNotSupported()
        => new("Remote hook RegisterLocal requires a result callback transport; use PluginServer.Hooks for local result handlers.");
}
