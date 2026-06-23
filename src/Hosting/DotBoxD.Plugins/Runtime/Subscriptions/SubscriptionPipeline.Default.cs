using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class SubscriptionPipeline<TEvent> : SubscriptionPipeline<TEvent, HookContext>
{
    internal SubscriptionPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<SubscriptionDeliveryFault>? onFault = null)
        : base(adapter, messages, ServerContextFactory<HookContext>.Default, kernels, installer, onFault)
    {
    }

    public new SubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        base.UseGeneratedChain(package);
        return this;
    }

    public new SubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
    {
        base.Where(filter);
        return this;
    }

    public new SubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        base.Where(filter);
        return this;
    }

    public new SubscriptionPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        base.Where(filter);
        return this;
    }

    public new SubscriptionPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        base.Where(filter);
        return this;
    }

    public new SubscriptionPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new SubscriptionPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new SubscriptionPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new SubscriptionPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        base.InvokeHostHandler(handler);
        return this;
    }

    public new SubscriptionPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public new SubscriptionPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public new SubscriptionPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public new SubscriptionPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    public new SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new SubscriptionStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public new SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    public new SubscriptionPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public new SubscriptionPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw Hooks.HookLowering.NotLowered();

    public new SubscriptionPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public new SubscriptionPipeline<TEvent> Run(Action<TEvent> handler)
        => throw Hooks.HookLowering.NotLowered();

    public new SubscriptionPipeline<TEvent> Use(InstalledKernel kernel)
    {
        base.Use(kernel);
        return this;
    }

    public new SubscriptionPipeline<TEvent> Use(InstalledKernelPool pool)
    {
        base.Use(pool);
        return this;
    }

    public new SubscriptionPipeline<TEvent> Use<TKernel>() where TKernel : class
    {
        base.Use<TKernel>();
        return this;
    }

    public new SubscriptionPipeline<TEvent> UseProjecting(
        InstalledKernel kernel,
        string subscriptionId,
        Hooks.RemoteLocalPush push)
    {
        base.UseProjecting(kernel, subscriptionId, push);
        return this;
    }
}
