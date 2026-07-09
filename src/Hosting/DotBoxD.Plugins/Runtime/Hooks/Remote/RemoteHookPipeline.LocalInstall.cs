namespace DotBoxD.Plugins.Runtime;

public sealed partial class RemoteHookPipeline<TEvent>
{
    /// <summary>
    /// Installs a lowered local-terminal package and registers <paramref name="handler"/> as the client-side
    /// terminal for the projected type <typeparamref name="TProjected"/>. Shared by this pipeline and by
    /// <see cref="Hooks.RemoteHookStage{TEvent, TCurrent}"/> (whose projected type is its <c>TCurrent</c>).
    /// </summary>
    internal RemoteHookPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateSubscription(package);
        LocalTerminalManifestValidator.ValidateRunLocal<TProjected>(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        var subscriptionId = LocalTerminalIdentity.CreateCallbackSubscriptionId();
        var registration = _localHandlers.Register(subscriptionId, handler);
        try
        {
            _install(LocalTerminalIdentity.WithCallbackSubscriptionId(package, subscriptionId))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            registration.Dispose();
            throw;
        }

        return this;
    }

    /// <summary>
    /// Installs a lowered local-terminal package and registers <paramref name="handler"/> with a generated
    /// reflection-free <paramref name="decoder"/> for the projected type <typeparamref name="TProjected"/>.
    /// </summary>
    internal RemoteHookPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler, Func<KernelRpcValue, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);
        ValidateSubscription(package);
        LocalTerminalManifestValidator.ValidateRunLocal<TProjected>(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        var subscriptionId = LocalTerminalIdentity.CreateCallbackSubscriptionId();
        var registration = _localHandlers.Register(subscriptionId, handler, decoder);
        try
        {
            _install(LocalTerminalIdentity.WithCallbackSubscriptionId(package, subscriptionId))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            registration.Dispose();
            throw;
        }

        return this;
    }

    internal RemoteHookPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);
        ValidateSubscription(package);
        LocalTerminalManifestValidator.ValidateRunLocal<TProjected>(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        var subscriptionId = LocalTerminalIdentity.CreateCallbackSubscriptionId();
        var registration = _localHandlers.Register(subscriptionId, handler, decoder);
        try
        {
            _install(LocalTerminalIdentity.WithCallbackSubscriptionId(package, subscriptionId))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            registration.Dispose();
            throw;
        }

        return this;
    }
}
