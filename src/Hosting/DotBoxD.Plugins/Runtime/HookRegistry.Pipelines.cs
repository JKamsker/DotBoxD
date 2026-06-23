using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime;

public sealed partial class HookRegistry
{
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

    private (object? Single, object[]? Multiple) PipelinesForEventLocked<TEvent>()
    {
        object? single = null;
        List<object>? multiple = null;
        foreach (var (key, pipeline) in _pipelines)
        {
            if (key.EventType != typeof(TEvent))
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

        return multiple is null ? (single, null) : (null, [.. multiple]);
    }

    private static async ValueTask PublishManyAsync<TEvent>(
        object[] pipelines,
        TEvent e,
        CancellationToken cancellationToken)
    {
        foreach (var pipeline in pipelines)
        {
            await ((IHookPipeline<TEvent>)pipeline).PublishAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<TResult?> FireManyAsync<TEvent, TResult>(
        object[] pipelines,
        TEvent e,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        foreach (var pipeline in pipelines)
        {
            var result = await ((IHookPipeline<TEvent>)pipeline)
                .FireResultAsync<TResult>(e, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static async ValueTask<TResult?> FireManyAsync<TEvent, TResult>(
        object[] pipelines,
        TEvent e,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        foreach (var pipeline in pipelines)
        {
            var result = await ((IHookPipeline<TEvent>)pipeline)
                .FireResultAsync(e, options, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
