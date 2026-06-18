using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class RemoteHookPipeline<TEvent>
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteHostCallbackRegistration, ValueTask<string>>? _installHostCallback;

    internal RemoteHookPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteHostCallbackRegistration, ValueTask<string>>? installHostCallback)
    {
        _install = install;
        _installHostCallback = installHostCallback;
    }

    public RemoteHookPipeline<TEvent> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteHookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateSubscription(package);
        _install(package).AsTask().GetAwaiter().GetResult();
        return this;
    }

    public RemoteHookPipeline<TEvent> UseGeneratedHostCallbackChain(
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

    public RemoteHookPipeline<TEvent> UseGeneratedHostCallbackChain(
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

    public RemoteHookPipeline<TEvent> UseGeneratedHostCallbackChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedHostCallbackChain(package, (e, _) => handler(e));
    }

    public RemoteHookPipeline<TEvent> UseGeneratedHostCallbackChain(PluginPackage package, Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedHostCallbackChain(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteHookPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteHookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(this);
    }

    public RemoteHookStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(this);
    }

    public RemoteHookPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Action<TEvent> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent> RunHost(Func<TEvent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunHost(Action<TEvent, HookContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunHost(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunHost(Action<TEvent> handler)
        => throw NotLowered();

    private static InvalidOperationException NotLowered()
        => new("Remote hook Run/RunHost lambda calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote hook RunLocal requires an event callback transport; use PluginServer.Hooks for local handlers.");

    private static NotSupportedException HostCallbacksNotSupported()
        => new("Remote hook RunHost requires a host callback transport.");

    private static void ValidateSubscription(PluginPackage package)
    {
        var actual = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0].Event : null;
        // Manifests now carry the fully-qualified event name; compare against typeof(TEvent).FullName but
        // accept the legacy simple-name form via EventNameMatch for back-compat.
        var expected = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        if (!EventNameMatch.Matches(actual, expected))
        {
            throw new InvalidOperationException(
                $"Hook package '{package.Manifest.PluginId}' subscribes to '{actual ?? "<none>"}', not '{expected}'.");
        }
    }
}
