using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class RemoteSubscriptionPipeline<TEvent, TContext>
{
    private readonly RemoteSubscriptionPipeline<TEvent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal RemoteSubscriptionPipeline(
        RemoteSubscriptionPipeline<TEvent> inner,
        Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        _inner.UseGeneratedChain(package);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _inner.Where((e, ctx) => filter(e, _contextFactory(ctx)));
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        _inner.Where(filter);
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext, TContext>(
            _inner.Select((e, ctx) => projection(e, _contextFactory(ctx))),
            _contextFactory);
    }

    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
        => new(_inner.Select(projection), _contextFactory);

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.UseGeneratedLocalChain(package, (e, ctx) => handler(e, _contextFactory(ctx)));
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalChain(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler)
    {
        _inner.UseGeneratedLocalChain(package, handler);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler)
    {
        _inner.UseGeneratedLocalChain(package, handler);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.UseGeneratedLocalChain(package, (e, ctx) => handler(e, _contextFactory(ctx)), decoder);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalChain(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        _inner.UseGeneratedLocalChain(package, handler, decoder);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        _inner.UseGeneratedLocalChain(package, handler, decoder);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.UseGeneratedLocalChain(package, (e, ctx) => handler(e, _contextFactory(ctx)), decoder);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalChain(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        _inner.UseGeneratedLocalChain(package, handler, decoder);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        _inner.UseGeneratedLocalChain(package, handler, decoder);
        return this;
    }

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");
}
