using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class HookPipeline<TEvent, TContext>
{
    private readonly HookPipeline<TEvent> _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal HookPipeline(HookPipeline<TEvent> inner, Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public HookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        _inner.UseGeneratedChain(package);
        return this;
    }

    public HookPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _inner.Where((e, ctx) => filter(e, _contextFactory(ctx)));
        return this;
    }

    public HookPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _inner.Where(filter);
        return this;
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Func<TEvent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InvokeHostHandler((e, ctx) => handler(e, _contextFactory(ctx)));
        return this;
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Action<TEvent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InvokeHostHandler(handler);
        return this;
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InvokeHostHandler(handler);
        return this;
    }

    public HookPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    public HookStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext, TContext>(
            _inner.Select((e, ctx) => projection(e, _contextFactory(ctx))),
            _contextFactory);
    }

    public HookStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext, TContext>(_inner.Select(projection), _contextFactory);
    }

    public HookPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Use(InstalledKernel kernel)
    {
        _inner.Use(kernel);
        return this;
    }

    public HookPipeline<TEvent, TContext> Use(InstalledKernelPool pool)
    {
        _inner.Use(pool);
        return this;
    }

    public HookPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
    {
        _inner.Use<TKernel>();
        return this;
    }

    public HookPipeline<TEvent, TContext> Register<TResult>(
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> Register<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> RegisterLocal<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public HookPipeline<TEvent, TContext> UseGeneratedResultChain<TResult>(
        PluginPackage package,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        _inner.UseGeneratedResultChain<TResult>(package, priority);
        return this;
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.UseGeneratedLocalResultChain<TResult>(
            package,
            (e, ctx) => handler(e, _contextFactory(ctx)),
            priority);
        return this;
    }

    public HookPipeline<TEvent, TContext> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner.UseGeneratedLocalResultChain<TResult>(package, handler, priority);
        return this;
    }
}
