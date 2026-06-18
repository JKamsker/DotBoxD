using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class RemoteSubscriptionPipeline<TEvent>
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteHostCallbackRegistration, ValueTask<string>>? _installHostCallback;

    internal RemoteSubscriptionPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteHostCallbackRegistration, ValueTask<string>>? installHostCallback)
    {
        _install = install;
        _installHostCallback = installHostCallback;
    }

    public RemoteSubscriptionPipeline<TEvent> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateSubscription(package);
        _install(package).AsTask().GetAwaiter().GetResult();
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedHostCallbackChain(
        PluginPackage package,
        Func<TEvent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        if (_installHostCallback is null)
        {
            throw HostCallbacksNotSupported();
        }

        ValidateSubscription(package);
        _installHostCallback(new RemoteHostCallbackRegistration(typeof(TEvent), package, handler))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedHostCallbackChain(
        PluginPackage package,
        Action<TEvent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedHostCallbackChain(package, (e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedHostCallbackChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedHostCallbackChain(package, (e, _) => handler(e));
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedHostCallbackChain(
        PluginPackage package,
        Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedHostCallbackChain(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteSubscriptionPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(this);
    }

    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(this);
    }

    public RemoteSubscriptionPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Action<TEvent> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunHost(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunHost(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunHost(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunHost(Action<TEvent> handler)
        => throw NotLowered();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run/RunHost lambda calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");

    private static NotSupportedException HostCallbacksNotSupported()
        => new("Remote subscription RunHost requires a host callback transport.");

    private static void ValidateSubscription(PluginPackage package)
    {
        var actual = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0].Event : null;
        // Manifests now carry the fully-qualified event name; compare against typeof(TEvent).FullName but
        // accept the legacy simple-name form via EventNameMatch for back-compat.
        var expected = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        if (!EventNameMatch.Matches(actual, expected))
        {
            throw new InvalidOperationException(
                $"Subscription package '{package.Manifest.PluginId}' subscribes to '{actual ?? "<none>"}', not '{expected}'.");
        }
    }
}
