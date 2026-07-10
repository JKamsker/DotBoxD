using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Abstractions;

/// <summary>Composes mergeable IR and preserves its local source documents and variable mappings.</summary>
public static class LoweredPipelineDebugComposer
{
    public static LoweredPipelineCompositionResult Compose(LoweredPipelineComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        var module = LoweredPipelineComposer.Compose(composition);
        var documents = Documents(composition.Steps);
        if (documents.Count == 0)
        {
            return new LoweredPipelineCompositionResult(module, debugInfo: null);
        }

        var bindings = VariableBindings(composition);
        return new LoweredPipelineCompositionResult(
            module,
            KernelDebugInfo.Create(module, documents, bindings));
    }

    private static IReadOnlyList<KernelDebugDocument> Documents(IReadOnlyList<LoweredPipelineStep> steps)
    {
        var documents = new Dictionary<string, KernelDebugDocument>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (step.DebugInfo is null)
            {
                continue;
            }

            foreach (var document in step.DebugInfo.Documents)
            {
                if (documents.TryGetValue(document.Id, out var existing) && existing != document)
                {
                    throw new ArgumentException($"Debug document ID '{document.Id}' has conflicting definitions.");
                }

                documents[document.Id] = document;
            }
        }

        return documents.Values.OrderBy(document => document.Id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<KernelDebugVariableBinding> VariableBindings(LoweredPipelineComposition composition)
    {
        var bindings = new List<KernelDebugVariableBinding>();
        AddShouldHandleBindings(composition, bindings);
        AddHandleBindings(composition, bindings);
        return bindings.Distinct().ToArray();
    }

    private static void AddShouldHandleBindings(
        LoweredPipelineComposition composition,
        List<KernelDebugVariableBinding> bindings)
    {
        var lastFilter = -1;
        for (var index = composition.Steps.Count - 1; index >= 0; index--)
        {
            if (composition.Steps[index].Kind == LoweredPipelineStepKind.Filter)
            {
                lastFilter = index;
                break;
            }
        }

        var current = 0;
        for (var index = 0; index <= lastFilter; index++)
        {
            AddBinding(composition.ShouldHandleFunctionId, current, composition.Steps[index], bindings);
            if (composition.Steps[index].Kind == LoweredPipelineStepKind.Projection)
            {
                current++;
            }
        }
    }

    private static void AddHandleBindings(
        LoweredPipelineComposition composition,
        List<KernelDebugVariableBinding> bindings)
    {
        var current = 0;
        foreach (var step in composition.Steps)
        {
            if (step.Kind != LoweredPipelineStepKind.Projection)
            {
                continue;
            }

            AddBinding(composition.HandleFunctionId, current, step, bindings);
            current++;
        }
    }

    private static void AddBinding(
        string functionId,
        int current,
        LoweredPipelineStep step,
        List<KernelDebugVariableBinding> bindings)
    {
        if (step.DebugInfo?.InputSourceName is { } sourceName)
        {
            bindings.Add(new KernelDebugVariableBinding(functionId, $"current{current}", sourceName));
        }
    }
}

