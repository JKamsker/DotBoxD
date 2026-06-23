using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class SubscriptionPipeline<TEvent, TContext>
{
    private readonly SubscriptionPipeline<TEvent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal SubscriptionPipeline(
        SubscriptionPipeline<TEvent> inner,
        Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        _inner.UseGeneratedChain(package);
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _inner.Where((e, ctx) => filter(e, _contextFactory(ctx)));
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        _inner.Where(filter);
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.RunLocal((e, ctx) => handler(e, _contextFactory(ctx)));
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
    {
        _inner.RunLocal(handler);
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
    {
        _inner.RunLocal(handler);
        return this;
    }

    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new SubscriptionStage<TEvent, TNext, TContext>(
            _inner.Select((e, ctx) => projection(e, _contextFactory(ctx))),
            _contextFactory);
    }

    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
        => new(_inner.Select(projection), _contextFactory);

    public SubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Use(InstalledKernel kernel)
    {
        _inner.Use(kernel);
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Use(InstalledKernelPool pool)
    {
        _inner.Use(pool);
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
    {
        _inner.Use<TKernel>();
        return this;
    }
}
