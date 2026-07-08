using System.ComponentModel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime.Subscriptions;

/// <summary>
/// A re-typed stage in a subscription chain after a <c>SubscriptionPipeline&lt;TEvent&gt;.Select</c>.
/// </summary>
[PipelineSurface(PipelineTransport.Local)]
public class SubscriptionStage<TEvent, TCurrent, TContext>
{
    private readonly SubscriptionPipeline<TEvent, TContext> _root;
    private readonly Func<TEvent, TContext, ValueTask<(bool Ok, TCurrent Value)>> _project;

    internal SubscriptionStage(
        SubscriptionPipeline<TEvent, TContext> root,
        Func<TEvent, TContext, ValueTask<(bool Ok, TCurrent Value)>> project)
    {
        _root = root;
        _project = project;
    }
    public SubscriptionStage<TEvent, TCurrent, TContext> Where(
        Func<TCurrent, TContext, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, TContext, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = irFilter?.Step;
        var project = _project;
        return new SubscriptionStage<TEvent, TCurrent, TContext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return (ok && filter(value, ctx), value);
        });
    }
    public SubscriptionStage<TEvent, TCurrent, TContext> Where(
        Func<TCurrent, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = irFilter?.Step;
        return Where((value, _) => filter(value));
    }
    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TContext, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _ = irProjection?.Step;
        var project = _project;
        return new SubscriptionStage<TEvent, TNext, TContext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return ok ? (true, projection(value, ctx)) : (false, default!);
        });
    }
    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _ = irProjection?.Step;
        return Select((value, _) => projection(value));
    }
    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
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
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }
    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var project = _project;
        return _root.UseGeneratedChain(package, async (e, ctx) =>
        {
            var (ok, _) = await project(e, ctx).ConfigureAwait(false);
            return ok;
        });
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChainFromInterceptor(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _root.UseGeneratedChain(package);
    }
    public SubscriptionPipeline<TEvent, TContext> Run(
        Func<TCurrent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChainFromInterceptor(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public SubscriptionPipeline<TEvent, TContext> Run(
        Action<TCurrent, TContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public SubscriptionPipeline<TEvent, TContext> Run(
        Func<TCurrent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, _) => handler(value), irHandler);
    }

    public SubscriptionPipeline<TEvent, TContext> Run(
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
}
