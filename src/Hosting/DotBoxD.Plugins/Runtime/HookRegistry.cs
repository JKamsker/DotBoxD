using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed class HookRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _pipelines = [];
    private readonly Dictionary<Type, Action<InstalledKernel>> _kernelRemovers = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal HookRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
        _installer = installer;
    }

    public HookPipeline<TEvent> On<TEvent>()
    {
        var adapter = _events.Resolve<TEvent>();
        return On(adapter);
    }

    public HookPipeline<TEvent> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            if (_pipelines.TryGetValue(typeof(TEvent), out var existing))
            {
                var pipeline = (HookPipeline<TEvent>)existing;
                if (!pipeline.UsesAdapter(adapter))
                {
                    throw new SandboxValidationException([
                        new SandboxDiagnostic(
                            "DBXK034",
                            $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                    ]);
                }

                return pipeline;
            }

            _events.Register(adapter);
            var created = new HookPipeline<TEvent>(adapter, _messages, _kernels, _installer);
            _pipelines[typeof(TEvent)] = created;
            _kernelRemovers[typeof(TEvent)] = created.RemoveKernel;
            return created;
        }
    }

    internal void RegisterEventAdapter<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        lock (_gate)
        {
            if (_pipelines.TryGetValue(typeof(TEvent), out var existing) &&
                !((HookPipeline<TEvent>)existing).UsesAdapter(adapter))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK034",
                        $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
                ]);
            }

            _events.Register(adapter);
        }
    }

    public async ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        object? pipeline;
        lock (_gate)
        {
            _pipelines.TryGetValue(typeof(TEvent), out pipeline);
        }

        if (pipeline is not null)
        {
            await ((HookPipeline<TEvent>)pipeline).PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void RemoveKernel(InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        Action<InstalledKernel>[] removers;
        lock (_gate)
        {
            removers = _kernelRemovers.Values.ToArray();
        }

        for (var i = 0; i < removers.Length; i++)
        {
            removers[i](kernel);
        }
    }
}
