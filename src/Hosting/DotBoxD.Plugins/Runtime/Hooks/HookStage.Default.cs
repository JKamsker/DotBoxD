namespace DotBoxD.Plugins.Runtime.Hooks;

public sealed class HookStage<TEvent, TCurrent>
{
    private readonly HookPipeline<TEvent> _root;
    private readonly Func<TEvent, HookContext, ValueTask<(bool Ok, TCurrent Value)>> _project;

    internal HookStage(
        HookPipeline<TEvent> root,
        Func<TEvent, HookContext, ValueTask<(bool Ok, TCurrent Value)>> project)
    {
        _root = root;
        _project = project;
    }

    public HookStage<TEvent, TCurrent> Where(Func<TCurrent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var project = _project;
        return new HookStage<TEvent, TCurrent>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return (ok && filter(value, ctx), value);
        });
    }

    public HookStage<TEvent, TCurrent> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((value, _) => filter(value));
    }

    public HookStage<TEvent, TNext> Select<TNext>(Func<TCurrent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var project = _project;
        return new HookStage<TEvent, TNext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return ok ? (true, projection(value, ctx)) : (false, default!);
        });
    }

    public HookStage<TEvent, TNext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((value, _) => projection(value));
    }

    public HookPipeline<TEvent> RunLocal(Func<TCurrent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var project = _project;
        return _root.RunLocal(async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            if (ok)
            {
                await handler(value, ctx).ConfigureAwait(false);
            }
        });
    }

    public HookPipeline<TEvent> RunLocal(Action<TCurrent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public HookPipeline<TEvent> RunLocal(Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public HookPipeline<TEvent> RunLocal(Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public HookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public HookPipeline<TEvent> Run(Func<TCurrent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Action<TCurrent, HookContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Func<TCurrent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Action<TCurrent> handler)
        => throw HookLowering.NotLowered();
}
