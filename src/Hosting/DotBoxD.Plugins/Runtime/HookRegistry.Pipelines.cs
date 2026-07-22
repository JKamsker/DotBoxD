using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

public sealed partial class HookRegistry
{
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

        EvictPipelineFanoutCaches();
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

        EvictPipelineFanoutCaches();
    }

    private void EvictPipelineFanoutCaches()
    {
        lock (_gate)
        {
            // Dropping the state owner prevents a pre-removal aggregate builder from republishing stale wrappers
            // through a cleared cache field. In-flight dispatches may finish against their stable old fanout.
            _pipelineFanout.Clear();
        }
    }

    internal HookPipeline<TEvent, HookContext> OnForWire<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        out bool created)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ThrowIfDisposed();
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(HookContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                created = false;
                var pipeline = (HookPipeline<TEvent, HookContext>)existing;
                EnsureContextFactoryMatches(pipeline.UsesContextFactory, ServerContextFactory<HookContext>.Identity, "hook");
                return pipeline;
            }

            created = true;
            var createdPipeline = new HookPipeline<TEvent, HookContext>(
                adapter,
                _messages,
                new ServerContextFactory<HookContext>(ServerContextFactory<HookContext>.Identity),
                _kernels,
                _installer,
                _onFault,
                NextResultOrder);
            _pipelines[key] = createdPipeline;
            RegisterEventTypeLocked<TEvent>();
            return createdPipeline;
        }
    }

    internal void RemoveWirePipeline<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        HookPipeline<TEvent, HookContext> pipeline)
    {
        lock (_gate)
        {
            var key = new PipelineKey(typeof(TEvent), typeof(HookContext));
            if (!_pipelines.TryGetValue(key, out var existing) ||
                !ReferenceEquals(existing, pipeline) ||
                !pipeline.UsesAdapter(adapter))
            {
                return;
            }

            _pipelines.Remove(key);
            var eventType = typeof(TEvent);
            _pipelineFanout.Remove(eventType);
            if (!HasPipelineForEventLocked(eventType))
            {
                _pipelineEventTypes.Remove(eventType);
            }
        }
    }

    private bool HasPipelineForEventLocked(Type eventType)
    {
        foreach (var key in _pipelines.Keys)
        {
            if (key.EventType == eventType)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureCanRegisterLocked<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        foreach (var (key, existing) in _pipelines)
        {
            if (key.EventType == typeof(TEvent) &&
                !((IHookPipeline<TEvent>)existing).UsesAdapter(adapter))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK034",
                        $"Hook pipeline for event '{typeof(TEvent).Name}' is already registered with a different adapter.")
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

    private (object? Single, CachedPipelineFanout Multiple) PipelinesForEventLocked<TEvent>()
    {
        var eventType = typeof(TEvent);
        if (!_pipelineEventTypes.Contains(eventType))
        {
            return (null, CachedPipelineFanout.Empty);
        }

        if (_pipelineFanout.TryGetValue(eventType, out var cached))
        {
            return cached;
        }

        object? single = null;
        List<object>? multiple = null;
        foreach (var (key, pipeline) in _pipelines)
        {
            if (key.EventType != eventType)
            {
                continue;
            }

            if (single is null && multiple is null)
            {
                single = pipeline;
                continue;
            }

            multiple ??= [single!];
            single = null;
            multiple.Add(pipeline);
        }

        (object? Single, CachedPipelineFanout Multiple) fanout = multiple is null
            ? (single, CachedPipelineFanout.Empty)
            : (null, CachedPipelineFanout.From(multiple));
        _pipelineFanout[eventType] = fanout;
        return fanout;
    }

    private void RegisterEventTypeLocked<TEvent>()
    {
        var eventType = typeof(TEvent);
        _pipelineEventTypes.Add(eventType);
        _pipelineFanout.Remove(eventType);
    }

    private static async ValueTask PublishManyAsync<TEvent>(
        CachedPipelineFanout pipelines,
        TEvent e,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (var i = 0; i < pipelines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ((IHookPipeline<TEvent>)pipelines[i]).PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<TResult?> FireManyAsync<TEvent, TResult>(
        CachedPipelineFanout pipelines,
        TEvent e,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        var registrations = Hooks.ResultHookRegistrationFanout.Ordered<TEvent>(pipelines);
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await registration
                .InvokeAsync<TResult>(e, options: null, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static async ValueTask<TResult?> FireManyAsync<TEvent, TResult>(
        CachedPipelineFanout pipelines,
        TEvent e,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        var registrations = Hooks.ResultHookRegistrationFanout.Ordered<TEvent>(pipelines);
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await registration
                .InvokeAsync(e, options, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

}
