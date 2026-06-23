using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins;

public sealed partial class PluginServer
{
    public PluginServer<THookContext, HookContext> WithHookContext<THookContext>(
        Func<HookContext, THookContext> contextFactory)
        => new(
            this,
            PluginContextFactory.Require(contextFactory, nameof(contextFactory)),
            PluginContextFactory.Identity);

    public PluginServer<HookContext, TSubscriptionContext> WithSubscriptionContext<TSubscriptionContext>(
        Func<HookContext, TSubscriptionContext> contextFactory)
        => new(
            this,
            PluginContextFactory.Identity,
            PluginContextFactory.Require(contextFactory, nameof(contextFactory)));
}

public sealed class PluginServer<THookContext, TSubscriptionContext> : IDisposable
{
    private readonly PluginServer _inner;
    private readonly Func<HookContext, THookContext> _hookContextFactory;
    private readonly Func<HookContext, TSubscriptionContext> _subscriptionContextFactory;

    internal PluginServer(
        PluginServer inner,
        Func<HookContext, THookContext> hookContextFactory,
        Func<HookContext, TSubscriptionContext> subscriptionContextFactory)
    {
        _inner = inner;
        _hookContextFactory = hookContextFactory;
        _subscriptionContextFactory = subscriptionContextFactory;
        Hooks = new HookRegistry<THookContext>(_inner.Hooks, _hookContextFactory);
        Subscriptions = new SubscriptionRegistry<TSubscriptionContext>(
            _inner.Subscriptions,
            _subscriptionContextFactory);
    }

    public HookRegistry<THookContext> Hooks { get; }
    public SubscriptionRegistry<TSubscriptionContext> Subscriptions { get; }
    public KernelRegistry Kernels => _inner.Kernels;
    public PluginEventAdapterRegistry Events => _inner.Events;
    public PluginServer Untyped => _inner;

    public PluginServer<TNextHookContext, TSubscriptionContext> WithHookContext<TNextHookContext>(
        Func<HookContext, TNextHookContext> contextFactory)
        => new(
            _inner,
            PluginContextFactory.Require(contextFactory, nameof(contextFactory)),
            _subscriptionContextFactory);

    public PluginServer<THookContext, TNextSubscriptionContext> WithSubscriptionContext<TNextSubscriptionContext>(
        Func<HookContext, TNextSubscriptionContext> contextFactory)
        => new(
            _inner,
            _hookContextFactory,
            PluginContextFactory.Require(contextFactory, nameof(contextFactory)));

    public IReadOnlyList<string> GetRequiredCapabilities(PluginPackage package)
        => _inner.GetRequiredCapabilities(package);

    public LiveValue<T> BindValue<T>(string name, T initialValue)
        => _inner.BindValue(name, initialValue);

    public LiveContext<T> BindContext<T>(string name, Action<T>? initialize = null)
        where T : class
        => _inner.BindContext(name, initialize);

    public PluginServer<THookContext, TSubscriptionContext> RegisterEventAdapter<TEvent>(
        IPluginEventAdapter<TEvent> adapter)
    {
        _inner.RegisterEventAdapter(adapter);
        return this;
    }

    public ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => _inner.InstallAsync(package, policy, cancellationToken);

    public ValueTask<InstalledKernelPool> InstallPoolAsync(
        PluginPackage package,
        int degreeOfParallelism,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => _inner.InstallPoolAsync(package, degreeOfParallelism, policy, cancellationToken);

    public ValueTask<InstalledKernel> InstallServerExtensionAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
        => _inner.InstallServerExtensionAsync(package, policy, cancellationToken);

    public PluginSession CreateSession() => _inner.CreateSession();
    public bool Uninstall(string pluginId) => _inner.Uninstall(pluginId);
    public bool UninstallPool(InstalledKernelPool pool) => _inner.UninstallPool(pool);
    public void Dispose() => _inner.Dispose();
}
