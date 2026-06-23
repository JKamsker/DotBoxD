namespace DotBoxD.Plugins.Runtime;

public sealed class HookRegistry<TContext>
{
    private readonly HookRegistry _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal HookRegistry(HookRegistry inner, Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public HookPipeline<TEvent, TContext> On<TEvent>()
        => new(_inner.On<TEvent>(), _contextFactory);

    public HookPipeline<TEvent, TContext> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => new(_inner.On(adapter), _contextFactory);

    public ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
        => _inner.PublishAsync(e, cancellationToken);

    public ValueTask<TResult?> FireAsync<TEvent, TResult>(
        TEvent context,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
        => _inner.FireAsync<TEvent, TResult>(context, cancellationToken);

    public ValueTask<TResult?> FireAsync<TEvent, TResult>(
        TEvent context,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
        => _inner.FireAsync(context, options, cancellationToken);
}

public sealed class SubscriptionRegistry<TContext>
{
    private readonly SubscriptionRegistry _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    internal SubscriptionRegistry(SubscriptionRegistry inner, Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = contextFactory;
    }

    public SubscriptionPipeline<TEvent, TContext> On<TEvent>()
        => new(_inner.On<TEvent>(), _contextFactory);

    public SubscriptionPipeline<TEvent, TContext> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
        => new(_inner.On(adapter), _contextFactory);

    public void Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
        => _inner.Publish(e, cancellationToken);

    public ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
        => _inner.PublishAsync(e, cancellationToken);
}

public sealed class RemoteHookRegistry<TContext>
{
    private readonly RemoteHookRegistry _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    public RemoteHookRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        Func<HookContext, TContext> contextFactory,
        Hooks.RemoteLocalHandlerRegistry? localHandlers = null)
        : this(new RemoteHookRegistry(install, localHandlers), contextFactory)
    {
    }

    internal RemoteHookRegistry(RemoteHookRegistry inner, Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = PluginContextFactory.Require(contextFactory, nameof(contextFactory));
    }

    public RemoteHookPipeline<TEvent, TContext> On<TEvent>()
        => new(_inner.On<TEvent>(), _contextFactory);
}

public sealed class RemoteSubscriptionRegistry<TContext>
{
    private readonly RemoteSubscriptionRegistry _inner;
    private readonly Func<HookContext, TContext> _contextFactory;

    public RemoteSubscriptionRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        Func<HookContext, TContext> contextFactory,
        Hooks.RemoteLocalHandlerRegistry? localHandlers = null)
        : this(new RemoteSubscriptionRegistry(install, localHandlers), contextFactory)
    {
    }

    internal RemoteSubscriptionRegistry(
        RemoteSubscriptionRegistry inner,
        Func<HookContext, TContext> contextFactory)
    {
        _inner = inner;
        _contextFactory = PluginContextFactory.Require(contextFactory, nameof(contextFactory));
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> On<TEvent>()
        => new(_inner.On<TEvent>(), _contextFactory);
}

public static class PluginRegistryContextExtensions
{
    public static HookRegistry<TContext> WithContext<TContext>(
        this HookRegistry registry,
        Func<HookContext, TContext> contextFactory)
        => new(registry, PluginContextFactory.Require(contextFactory, nameof(contextFactory)));

    public static SubscriptionRegistry<TContext> WithContext<TContext>(
        this SubscriptionRegistry registry,
        Func<HookContext, TContext> contextFactory)
        => new(registry, PluginContextFactory.Require(contextFactory, nameof(contextFactory)));

    public static RemoteHookRegistry<TContext> WithContext<TContext>(
        this RemoteHookRegistry registry,
        Func<HookContext, TContext> contextFactory)
        => new(registry, contextFactory);

    public static RemoteSubscriptionRegistry<TContext> WithContext<TContext>(
        this RemoteSubscriptionRegistry registry,
        Func<HookContext, TContext> contextFactory)
        => new(registry, contextFactory);
}
