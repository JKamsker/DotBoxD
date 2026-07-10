namespace DotBoxD.Plugins.Runtime;

internal sealed class RemotePipelineIr
{
    private static readonly LoweredPipelineStep[] NoSteps = [];

    private readonly LoweredPipelineStep[] _steps;

    private RemotePipelineIr(LoweredPipelineStep[] steps)
        => _steps = steps;

    public static RemotePipelineIr Empty { get; } = new(NoSteps);

    public bool HasSteps => _steps.Length > 0;

    public RemotePipelineIr Append<TInput, TOutput>(
        IRFunc<TInput, TOutput>? irFunc,
        string parameterName)
        => AppendStep(RequiredStep(irFunc, parameterName));

    public RemotePipelineIr Append<TInput, TContext, TOutput>(
        IRFunc<TInput, TContext, TOutput>? irFunc,
        string parameterName)
        => AppendStep(RequiredStep(irFunc, parameterName));

    public PluginPackage ComposeLocalTerminalPackage(IRKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (!HasSteps)
        {
            return kernel.Package;
        }

        var package = kernel.Package;
        var handle = HandleFunction(package);
        if (!CanComposeWithPackageShape(package, handle))
        {
            return package;
        }

        var composed = LoweredPipelineDebugComposer.Compose(new LoweredPipelineComposition(
            package.Module.Id,
            _steps,
            handle.ReturnType)
        {
            Version = package.Module.Version,
            TargetSandboxVersion = package.Module.TargetSandboxVersion,
            ShouldHandleFunctionId = package.Entrypoints.ShouldHandle,
            HandleFunctionId = package.Entrypoints.Handle,
        });
        var module = composed.Module;
        module = module with { Metadata = Merge(package.Module.Metadata, module.Metadata) };

        return PluginPackage.Create(
            MergeManifestStepMetadata(package.Manifest),
            module,
            package.Entrypoints,
            composed.DebugInfo);
    }

    private RemotePipelineIr AppendStep(LoweredPipelineStep step)
    {
        var copy = new LoweredPipelineStep[_steps.Length + 1];
        Array.Copy(_steps, copy, _steps.Length);
        copy[^1] = step;
        return new RemotePipelineIr(copy);
    }

    private PluginManifest MergeManifestStepMetadata(PluginManifest manifest)
        => manifest with
        {
            RequiredCapabilities = Merge(manifest.RequiredCapabilities, _steps.SelectMany(step => step.RequiredCapabilities)),
            Effects = Merge(manifest.Effects, _steps.SelectMany(step => step.Effects)),
        };

    private bool CanComposeWithPackageShape(PluginPackage package, SandboxFunction handle)
    {
        var shouldHandle = package.Module.Functions.FirstOrDefault(function =>
            string.Equals(function.Id, package.Entrypoints.ShouldHandle, StringComparison.Ordinal));
        var inputType = _steps[0].Parameters.Count == 1 ? _steps[0].Parameters[0].Type : null;
        return inputType is not null &&
            shouldHandle?.Parameters.Count == 1 &&
            handle.Parameters.Count == 1 &&
            shouldHandle.Parameters[0].Type == inputType &&
            handle.Parameters[0].Type == inputType;
    }

    private static SandboxFunction HandleFunction(PluginPackage package)
        => package.Module.Functions.FirstOrDefault(function =>
            string.Equals(function.Id, package.Entrypoints.Handle, StringComparison.Ordinal)) ??
        throw new InvalidOperationException(
            $"IR kernel package '{package.Manifest.PluginId}' does not define handle entrypoint " +
            $"'{package.Entrypoints.Handle}'.");

    private static IReadOnlyList<string> Merge(
        IReadOnlyList<string> existing,
        IEnumerable<string> generated)
    {
        var merged = new List<string>(existing.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in existing.Concat(generated))
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                merged.Add(value);
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string> generated)
    {
        var merged = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        foreach (var item in generated)
        {
            merged[item.Key] = item.Value;
        }

        return merged;
    }

    private static LoweredPipelineStep RequiredStep<TInput, TOutput>(
        IRFunc<TInput, TOutput>? irFunc,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(irFunc, parameterName);
        return irFunc.Step;
    }

    private static LoweredPipelineStep RequiredStep<TInput, TContext, TOutput>(
        IRFunc<TInput, TContext, TOutput>? irFunc,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(irFunc, parameterName);
        return irFunc.Step;
    }
}
