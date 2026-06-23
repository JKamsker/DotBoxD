namespace DotBoxD.Plugins.Runtime.Subscriptions;

public sealed class RemoteSubscriptionStage<TEvent, TCurrent, TContext>
{
    private readonly RemoteSubscriptionStage<TEvent, TCurrent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal RemoteSubscriptionStage(
        RemoteSubscriptionStage<TEvent, TCurrent> inner,
        Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public RemoteSubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _inner.Where((value, ctx) => filter(value, _contextFactory(ctx)));
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
    {
        _inner.Where(filter);
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext, TContext>(
            _inner.Select((value, ctx) => projection(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TNext> projection)
        => new(_inner.Select(projection), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => new(_inner.UseGeneratedChain(package), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new RemoteSubscriptionPipeline<TEvent, TContext>(
            _inner.UseGeneratedLocalChain(package, (value, ctx) => handler(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler)
        => new(_inner.UseGeneratedLocalChain(package, handler), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler)
        => new(_inner.UseGeneratedLocalChain(package, handler), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new RemoteSubscriptionPipeline<TEvent, TContext>(
            _inner.UseGeneratedLocalChain(package, (value, ctx) => handler(value, _contextFactory(ctx)), decoder),
            _contextFactory);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new RemoteSubscriptionPipeline<TEvent, TContext>(
            _inner.UseGeneratedLocalChain(package, (value, ctx) => handler(value, _contextFactory(ctx)), decoder),
            _contextFactory);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => new(_inner.UseGeneratedLocalChain(package, handler, decoder), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
        => throw LocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");
}
