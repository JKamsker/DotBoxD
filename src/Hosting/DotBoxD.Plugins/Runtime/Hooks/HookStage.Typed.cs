namespace DotBoxD.Plugins.Runtime.Hooks;

public sealed class HookStage<TEvent, TCurrent, TContext>
{
    private readonly HookStage<TEvent, TCurrent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal HookStage(HookStage<TEvent, TCurrent> inner, Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public HookStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new HookStage<TEvent, TCurrent, TContext>(
            _inner.Where((value, ctx) => filter(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public HookStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new HookStage<TEvent, TCurrent, TContext>(_inner.Where(filter), _contextFactory);
    }

    public HookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext, TContext>(
            _inner.Select((value, ctx) => projection(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public HookStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext, TContext>(_inner.Select(projection), _contextFactory);
    }

    public HookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new HookPipeline<TEvent, TContext>(
            _inner.RunLocal((value, ctx) => handler(value, _contextFactory(ctx))),
            _contextFactory);
    }

    public HookPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public HookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new HookPipeline<TEvent, TContext>(_inner.RunLocal(handler), _contextFactory);
    }

    public HookPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new HookPipeline<TEvent, TContext>(_inner.RunLocal(handler), _contextFactory);
    }

    public HookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => new(_inner.UseGeneratedChain(package), _contextFactory);

    public HookPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw HookLowering.NotLowered();

}
