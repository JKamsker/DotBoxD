using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

internal interface ISubscriptionPipeline<TEvent> : IKernelHandlerPipeline
{
    bool UsesAdapter(IPluginEventAdapter<TEvent> adapter);
    void Publish(TEvent e, CancellationToken cancellationToken);
}

public sealed class SubscriptionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<PipelineKey, object> _pipelines = [];

    // Writers clone under _gate and publish the completed dictionary; a published instance is never mutated.
    private Dictionary<Type, CachedPipelineFanout> _pipelineFanout = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;
    private readonly Action<SubscriptionDeliveryFault>? _onFault;
    private readonly Action? _throwIfDisposed;

    internal SubscriptionRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<SubscriptionDeliveryFault>? onFault = null,
        Action? throwIfDisposed = null)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
        _installer = installer;
        _onFault = onFault;
        _throwIfDisposed = throwIfDisposed;
    }
    public SubscriptionPipeline<TEvent, HookContext> On<TEvent>()
    {
        ThrowIfDisposed();
        var adapter = _events.Resolve<TEvent>();
        return On(adapter);
    }
    public SubscriptionPipeline<TEvent, HookContext> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ThrowIfDisposed();
        return OnHookContext(adapter, ServerContextFactory<HookContext>.Identity);
    }
    public SubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(Func<HookContext, TContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        ThrowIfDisposed();
        var adapter = _events.Resolve<TEvent>();
        return On(adapter, createContext);
    }
    public SubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(
        IPluginEventAdapter<TEvent> adapter,
        Func<HookContext, TContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(createContext);
        ThrowIfDisposed();
        if (typeof(TContext) == typeof(HookContext))
        {
            return (SubscriptionPipeline<TEvent, TContext>)(object)OnHookContext(
                adapter,
                (Func<HookContext, HookContext>)(object)createContext);
        }

        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(TContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                var pipeline = (SubscriptionPipeline<TEvent, TContext>)existing;
                EnsureContextFactoryMatches(pipeline.UsesContextFactory, createContext, "subscription");
                return pipeline;
            }

            var created = new SubscriptionPipeline<TEvent, TContext>(
                adapter,
                _messages,
                new ServerContextFactory<TContext>(createContext),
                _kernels,
                _installer,
                _onFault,
                _throwIfDisposed);
            _pipelines[key] = created;
            PublishEventFanoutLocked(typeof(TEvent));
            return created;
        }
    }

    private SubscriptionPipeline<TEvent, HookContext> OnHookContext<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        Func<HookContext, HookContext> createContext)
    {
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(HookContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                var pipeline = (SubscriptionPipeline<TEvent, HookContext>)existing;
                EnsureContextFactoryMatches(pipeline.UsesContextFactory, createContext, "subscription");
                return pipeline;
            }

            var created = new SubscriptionPipeline<TEvent, HookContext>(
                adapter,
                _messages,
                new ServerContextFactory<HookContext>(createContext),
                _kernels,
                _installer,
                _onFault,
                _throwIfDisposed);
            _pipelines[key] = created;
            PublishEventFanoutLocked(typeof(TEvent));
            return created;
        }
    }

    internal void EnsureCanRegister<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
        }
    }

    internal void RemoveKernel(InstalledKernel kernel)
    {
        object[] pipelines;
        lock (_gate)
        {
            pipelines = [.. _pipelines.Values];
        }

        foreach (var pipeline in pipelines)
        {
            ((IKernelHandlerPipeline)pipeline).RemoveKernel(kernel);
        }
    }

    internal void RemoveKernelPool(InstalledKernelPool pool)
    {
        object[] pipelines;
        lock (_gate)
        {
            pipelines = [.. _pipelines.Values];
        }

        foreach (var pipeline in pipelines)
        {
            ((IKernelHandlerPipeline)pipeline).RemoveKernelPool(pool);
        }
    }

    public void Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var pipelines = PipelinesForEvent<TEvent>();

        foreach (var pipeline in pipelines)
        {
            ((ISubscriptionPipeline<TEvent>)pipeline).Publish(e, cancellationToken);
        }
    }

    public ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        Publish(e, cancellationToken);
        return ValueTask.CompletedTask;
    }

    private void EnsureCanRegisterLocked<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        foreach (var (key, existing) in _pipelines)
        {
            if (key.EventType == typeof(TEvent) &&
                !((ISubscriptionPipeline<TEvent>)existing).UsesAdapter(adapter))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK064",
                        $"Subscription pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                ]);
            }
        }
    }

    private static void EnsureContextFactoryMatches<TContext>(
        Func<Func<HookContext, TContext>, bool> usesContextFactory,
        Func<HookContext, TContext> createContext,
        string surface)
    {
        if (usesContextFactory(createContext))
        {
            return;
        }

        throw new SandboxValidationException([
            new SandboxDiagnostic(
                "DBXK067",
                $"A {surface} pipeline for context '{typeof(TContext).Name}' is already registered with a different context factory.")
        ]);
    }

    private CachedPipelineFanout PipelinesForEvent<TEvent>()
    {
        var snapshot = Volatile.Read(ref _pipelineFanout);
        return snapshot.TryGetValue(typeof(TEvent), out var fanout)
            ? fanout
            : CachedPipelineFanout.Empty;
    }

    private void PublishEventFanoutLocked(Type eventType)
    {
        List<object>? pipelines = null;
        foreach (var (key, pipeline) in _pipelines)
        {
            if (key.EventType == eventType)
            {
                pipelines ??= [];
                pipelines.Add(pipeline);
            }
        }

        var fanout = CachedPipelineFanout.From(pipelines);
        var replacement = new Dictionary<Type, CachedPipelineFanout>(Volatile.Read(ref _pipelineFanout));
        replacement[eventType] = fanout;
        Volatile.Write(ref _pipelineFanout, replacement);
    }

    private void ThrowIfDisposed()
        => _throwIfDisposed?.Invoke();

    private readonly record struct PipelineKey(Type EventType, Type ContextType);
}
