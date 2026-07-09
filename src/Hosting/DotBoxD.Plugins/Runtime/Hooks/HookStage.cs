using System.ComponentModel;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// A re-typed stage in a hook chain after a <c>HookPipeline&lt;TEvent&gt;.Select</c>. It
/// carries a composed projection of the original event to the element currently flowing
/// (<typeparamref name="TCurrent"/>) plus a short-circuit flag, so <c>Where</c>/<c>Select</c> compose
/// without re-keying the pipeline (it stays keyed by <typeparamref name="TEvent"/>). Terminals
/// re-run the projection per publish and short-circuit when a <c>Where</c> rejected the element.
/// Every fluent method offers both the (element, context) and element-only lambda arities, chosen
/// independently per stage.
/// </summary>
[PipelineSurface(PipelineTransport.Local)]
public class HookStage<TEvent, TCurrent, TContext>
{
    private readonly HookPipeline<TEvent, TContext> _root;
    private readonly Func<TEvent, TContext, ValueTask<(bool Ok, TCurrent Value)>> _project;

    internal HookStage(
        HookPipeline<TEvent, TContext> root,
        Func<TEvent, TContext, ValueTask<(bool Ok, TCurrent Value)>> project)
    {
        _root = root;
        _project = project;
    }
    public HookStage<TEvent, TCurrent, TContext> Where(
        Func<TCurrent, TContext, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, TContext, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = irFilter?.Step;
        var project = _project;
        return new HookStage<TEvent, TCurrent, TContext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return (ok && filter(value, ctx), value);
        });
    }

    /// <summary>Element-only filter over the projected element — the (element, context) overload with
    /// the context discarded, so a stage need not take the context it doesn't use.</summary>
    public HookStage<TEvent, TCurrent, TContext> Where(
        Func<TCurrent, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TCurrent, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _ = irFilter?.Step;
        return Where((value, _) => filter(value));
    }
    public HookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TContext, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _ = irProjection?.Step;
        var project = _project;
        return new HookStage<TEvent, TNext, TContext>(_root, async (e, ctx) =>
        {
            var (ok, value) = await project(e, ctx).ConfigureAwait(false);
            return ok ? (true, projection(value, ctx)) : (false, default!);
        });
    }
    public HookStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TCurrent, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _ = irProjection?.Step;
        return Select((value, _) => projection(value));
    }

    /// <summary>Native host terminal over the projected element (NOT sandboxed).</summary>
    public HookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
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
        return RunLocal((value, _) => handler(value));
    }
    public HookPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((value, _) => handler(value));
    }

    public HookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
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
    public HookPipeline<TEvent, TContext> UseGeneratedChainFromInterceptor(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _root.UseGeneratedChain(package);
    }

    /// <summary>The terminal the analyzer lowers to verified IR; un-lowered it throws (never native).</summary>
    public HookPipeline<TEvent, TContext> Run(
        Func<TCurrent, TContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChainFromInterceptor(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public HookPipeline<TEvent, TContext> Run(
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

    public HookPipeline<TEvent, TContext> Run(
        Func<TCurrent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((value, _) => handler(value), irHandler);
    }

    public HookPipeline<TEvent, TContext> Run(
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

internal static class HookLowering
{
    public static PluginPackage RequiredPackage(IRKernel? kernel, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(kernel, parameterName);
        return kernel.Package;
    }

    public static SandboxValidationException NotLowered()
        => new([
            new SandboxDiagnostic(
                "DBXK062",
                "Run(lambda) must be lowered to verified IR by DotBoxD.Plugins.Analyzer and cannot run as host code.")
        ]);

    public static SandboxValidationException ResultNotLowered()
        => new([
            new SandboxDiagnostic(
                "DBXK062",
                "Register(lambda)/RegisterLocal(lambda) must be lowered to verified IR by DotBoxD.Plugins.Analyzer "
                + "and cannot run as host code.")
        ]);
}
