using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime.Subscriptions;

public sealed class SubscriptionStage<TEvent, TCurrent, TContext>
{
    private readonly SubscriptionStage<TEvent, TCurrent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal SubscriptionStage(
        SubscriptionStage<TEvent, TCurrent> inner,
        Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public SubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new SubscriptionStage<TEvent, TCurrent, TContext>(
            _inner.Where((value, ctx) => filter(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public SubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
        => new(_inner.Where(filter), _contextFactory);

    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new SubscriptionStage<TEvent, TNext, TContext>(
            _inner.Select((value, ctx) => projection(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TNext> projection)
        => new(_inner.Select(projection), _contextFactory);

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new SubscriptionPipeline<TEvent, TContext>(
            _inner.RunLocal((value, ctx) => handler(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
        => new(_inner.RunLocal(handler), _contextFactory);

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
        => new(_inner.RunLocal(handler), _contextFactory);

    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => new(_inner.UseGeneratedChain(package), _contextFactory);

    public SubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw HookLowering.NotLowered();
}
