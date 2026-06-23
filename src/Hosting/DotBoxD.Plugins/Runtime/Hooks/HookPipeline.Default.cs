using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class HookPipeline<TEvent> : HookPipeline<TEvent, HookContext>
{
    internal HookPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<ResultHookFault>? onFault = null)
        : base(adapter, messages, ServerContextFactory<HookContext>.Default, kernels, installer, onFault)
    {
    }

    public new HookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        base.UseGeneratedChain(package);
        return this;
    }

    public new HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
    {
        base.Where(filter);
        return this;
    }

    public new HookPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        base.Where(filter);
        return this;
    }

    public new HookPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        base.Where(filter);
        return this;
    }

    public new HookPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        base.Where(filter);
        return this;
    }

    public new HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new HookPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new HookPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new HookPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public new HookPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public new HookPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public new HookPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    public new HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public new HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    public new HookPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public new HookPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw HookLowering.NotLowered();

    public new HookPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public new HookPipeline<TEvent> Run(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public new HookPipeline<TEvent> Use(InstalledKernel kernel)
    {
        base.Use(kernel);
        return this;
    }

    public new HookPipeline<TEvent> Use(InstalledKernelPool pool)
    {
        base.Use(pool);
        return this;
    }

    public new HookPipeline<TEvent> Use<TKernel>() where TKernel : class
    {
        base.Use<TKernel>();
        return this;
    }

    public new HookPipeline<TEvent> UseProjecting(InstalledKernel kernel, string subscriptionId, RemoteLocalPush push)
    {
        base.UseProjecting(kernel, subscriptionId, push);
        return this;
    }

    public new HookPipeline<TEvent> Register<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public new HookPipeline<TEvent> Register<TResult>(
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public new HookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public new HookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public new HookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
        => throw HookLowering.ResultNotLowered();

    public new HookPipeline<TEvent> UseGeneratedResultChain<TResult>(PluginPackage package, int priority = 0)
        where TResult : struct, IHookResult
    {
        base.UseGeneratedResultChain<TResult>(package, priority);
        return this;
    }

    public new HookPipeline<TEvent> UseResult(InstalledKernel kernel, Type resultType, int priority = 0)
    {
        base.UseResult(kernel, resultType, priority);
        return this;
    }

    public new HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        base.UseGeneratedLocalResultChain(package, handler, priority);
        return this;
    }

    public new HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        base.UseGeneratedLocalResultChain(package, handler, priority);
        return this;
    }

    public new HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        base.UseGeneratedLocalResultChain(package, handler, priority);
        return this;
    }

    public new HookPipeline<TEvent> UseProjectingResult(
        InstalledKernel filterKernel,
        string subscriptionId,
        Type resultType,
        RemoteLocalResultRequest request,
        int priority = 0)
    {
        base.UseProjectingResult(filterKernel, subscriptionId, resultType, request, priority);
        return this;
    }

    public new HookPipeline<TEvent> UseProjectingResult<TResult>(
        InstalledKernel filterKernel,
        string subscriptionId,
        RemoteLocalResultRequest request,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        base.UseProjectingResult<TResult>(filterKernel, subscriptionId, request, priority);
        return this;
    }

    public new HookPipeline<TEvent> ConfigureResultDispatch<TResult>(ResultHookDispatchOptions<TResult> options)
        where TResult : struct, IHookResult
    {
        base.ConfigureResultDispatch(options);
        return this;
    }
}
