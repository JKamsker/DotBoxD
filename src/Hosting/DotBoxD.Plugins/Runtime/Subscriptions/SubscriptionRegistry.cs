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
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;
    private readonly Action<SubscriptionDeliveryFault>? _onFault;

    internal SubscriptionRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<SubscriptionDeliveryFault>? onFault = null)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
        _installer = installer;
        _onFault = onFault;
    }

    public SubscriptionPipeline<TEvent> On<TEvent>()
    {
        var adapter = _events.Resolve<TEvent>();
        return On(adapter);
    }

    public SubscriptionPipeline<TEvent> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(HookContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                return (SubscriptionPipeline<TEvent>)existing;
            }

            var created = new SubscriptionPipeline<TEvent>(adapter, _messages, _kernels, _installer, _onFault);
            _pipelines[key] = created;
            return created;
        }
    }

    public SubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(Func<HookContext, TContext> createContext)
    {
        var adapter = _events.Resolve<TEvent>();
        return On(adapter, createContext);
    }

    public SubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(
        IPluginEventAdapter<TEvent> adapter,
        Func<HookContext, TContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(TContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                return (SubscriptionPipeline<TEvent, TContext>)existing;
            }

            var created = new SubscriptionPipeline<TEvent, TContext>(
                adapter,
                _messages,
                new ServerContextFactory<TContext>(createContext),
                _kernels,
                _installer,
                _onFault);
            _pipelines[key] = created;
            return created;
        }
    }

    internal void EnsureCanRegister<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
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
        object[] pipelines;
        lock (_gate)
        {
            pipelines = PipelinesForEventLocked<TEvent>();
        }

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

    private object[] PipelinesForEventLocked<TEvent>()
    {
        var pipelines = new List<object>();
        foreach (var (key, pipeline) in _pipelines)
        {
            if (key.EventType == typeof(TEvent))
            {
                pipelines.Add(pipeline);
            }
        }

        return [.. pipelines];
    }

    private readonly record struct PipelineKey(Type EventType, Type ContextType);
}
