using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

[PipelineSurface(PipelineTransport.Remote)]
public sealed class RemoteSubscriptionPipeline<TEvent>
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly RemoteLocalHandlerRegistry? _localHandlers;
    private readonly RemotePipelineIr _pipelineIr;

    internal RemoteSubscriptionPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        RemoteLocalHandlerRegistry? localHandlers = null)
        : this(install, localHandlers, RemotePipelineIr.Empty)
    {
    }

    private RemoteSubscriptionPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        RemoteLocalHandlerRegistry? localHandlers,
        RemotePipelineIr pipelineIr)
    {
        _install = install;
        _localHandlers = localHandlers;
        _pipelineIr = pipelineIr;
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

    /// <summary>
    /// Installs a lowered <c>RunLocal</c> subscription chain: the lowered <c>Where</c>/<c>Select</c>
    /// filter+projection installs server-side and the native <paramref name="handler"/> is registered against
    /// the returned subscription id to receive each filtered, projected value pushed back over IPC.
    /// </summary>
    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, HookContext, ValueTask> handler)
        => InstallLocal(package, handler);

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e));
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    // Decoder overloads: a whole-event RunLocal subscription whose event type is wire-eligible installs with the
    // generated reflection-free decoder, emitted by the interceptor as the 3rd argument.
    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, HookContext, ValueTask> handler, Func<KernelRpcValue, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent, HookContext> handler, Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, ValueTask> handler, Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler, Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent, HookContext> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TEvent, ValueTask> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler, Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    internal RemoteSubscriptionPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler)
    {
        var (subscriptionId, handlers) = PrepareLocalInstall(package, handler);
        var registration = handlers.Register(subscriptionId, handler);
        InstallWithRegistration(package, subscriptionId, registration);
        return this;
    }

    internal RemoteSubscriptionPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler, Func<KernelRpcValue, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var (subscriptionId, handlers) = PrepareLocalInstall(package, handler);
        var registration = handlers.Register(subscriptionId, handler, decoder);
        InstallWithRegistration(package, subscriptionId, registration);
        return this;
    }

    internal RemoteSubscriptionPipeline<TEvent> InstallLocal<TProjected>(PluginPackage package, Func<TProjected, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        var (subscriptionId, handlers) = PrepareLocalInstall(package, handler);
        var registration = handlers.Register(subscriptionId, handler, decoder);
        InstallWithRegistration(package, subscriptionId, registration);
        return this;
    }

    private (string SubscriptionId, RemoteLocalHandlerRegistry Handlers) PrepareLocalInstall<TProjected>(
        PluginPackage package,
        Func<TProjected, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateSubscription(package);
        LocalTerminalManifestValidator.ValidateRunLocal<TProjected>(package);
        if (_localHandlers is null)
        {
            throw LocalHandlersNotSupported();
        }

        return (LocalTerminalIdentity.CreateCallbackSubscriptionId(), _localHandlers);
    }

    private void InstallWithRegistration(PluginPackage package, string subscriptionId, IDisposable registration)
    {
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
    }
    public RemoteSubscriptionPipeline<TEvent> Where(
        Func<TEvent, HookContext, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TEvent, HookContext, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return AppendStep(irFilter, nameof(irFilter));
    }
    public RemoteSubscriptionPipeline<TEvent> Where(
        Func<TEvent, bool> filter,
        [IRBodyOf(nameof(filter))] IRFunc<TEvent, bool>? irFilter = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return AppendStep(irFilter, nameof(irFilter));
    }
    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(
        Func<TEvent, HookContext, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TEvent, HookContext, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(
            AppendStep(irProjection, nameof(irProjection)));
    }
    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(
        Func<TEvent, TNext> projection,
        [IRBodyOf(nameof(projection))] IRFunc<TEvent, TNext>? irProjection = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(
            AppendStep(irProjection, nameof(irProjection)));
    }
    public RemoteSubscriptionPipeline<TEvent> Run(
        Func<TEvent, HookContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedChain(HookLowering.RequiredPackage(irHandler, nameof(irHandler)));
    }

    public RemoteSubscriptionPipeline<TEvent> Run(
        Action<TEvent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteSubscriptionPipeline<TEvent> Run(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, _) => handler(e), irHandler);
    }

    public RemoteSubscriptionPipeline<TEvent> Run(
        Action<TEvent> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Run((e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteSubscriptionPipeline<TEvent> RunLocal(
        Func<TEvent, HookContext, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = RequiredKernel(irHandler);
        var package = LocalTerminalPackage(kernel);
        return kernel.TryGetProjectedPayloadDecoder<TEvent>(out var decoder)
            ? InstallLocal(package, handler, decoder)
            : InstallLocal(package, handler);
    }

    public RemoteSubscriptionPipeline<TEvent> RunLocal(
        Action<TEvent, HookContext> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    public RemoteSubscriptionPipeline<TEvent> RunLocal(
        Func<TEvent, ValueTask> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, _) => handler(e), irHandler);
    }

    public RemoteSubscriptionPipeline<TEvent> RunLocal(
        Action<TEvent> handler,
        [IRBodyOf(nameof(handler))] IRKernel? irHandler = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RunLocal((e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, irHandler);
    }

    private static IRKernel RequiredKernel(IRKernel? irHandler)
        => irHandler ?? throw new ArgumentNullException(nameof(irHandler));

    internal RemoteSubscriptionPipeline<TEvent> AppendStep<TInput, TOutput>(IRFunc<TInput, TOutput>? irFunc, string parameterName)
        => WithPipelineIr(_pipelineIr.Append(irFunc, parameterName));

    internal RemoteSubscriptionPipeline<TEvent> AppendStep<TInput, TContext, TOutput>(IRFunc<TInput, TContext, TOutput>? irFunc, string parameterName)
        => WithPipelineIr(_pipelineIr.Append(irFunc, parameterName));

    internal PluginPackage LocalTerminalPackage(IRKernel kernel)
        => _pipelineIr.ComposeLocalTerminalPackage(kernel);

    private RemoteSubscriptionPipeline<TEvent> WithPipelineIr(RemotePipelineIr pipelineIr)
        => new(_install, _localHandlers, pipelineIr);

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");

    private static void ValidateSubscription(PluginPackage package)
    {
        var actual = package.Manifest.Subscriptions.Count > 0 ? package.Manifest.Subscriptions[0].Event : null;
        // Manifests now carry the fully-qualified event name; compare against typeof(TEvent).FullName but
        // accept the legacy simple-name form via EventNameMatch for back-compat.
        var expected = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        if (!EventNameMatch.Matches(actual, expected) && !HookNameMatches(actual))
        {
            throw new InvalidOperationException(
                $"Subscription package '{package.Manifest.PluginId}' subscribes to '{actual ?? "<none>"}', not '{expected}'.");
        }
    }

    private static bool HookNameMatches(string? actual)
    {
        var hook = (HookAttribute?)Attribute.GetCustomAttribute(
            typeof(TEvent),
            typeof(HookAttribute),
            inherit: false);

        return hook is not null &&
            !string.IsNullOrEmpty(actual) &&
            string.Equals(hook.Name, actual, StringComparison.Ordinal);
    }
}
