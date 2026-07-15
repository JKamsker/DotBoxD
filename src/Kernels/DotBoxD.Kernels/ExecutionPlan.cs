using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels;

using System.Collections.Frozen;

public sealed record FunctionAnalysis(SandboxType ReturnType, SandboxEffect Effects, bool CanReorder);

public sealed class ExecutionPlan
{
    private ExecutionPlanEntrypointMetadata? _moduleOnlyEntrypointMetadata;
    private FrozenDictionary<string, ExecutionPlanEntrypointMetadata>? _entrypointMetadataLookup;
    private FrozenDictionary<string, SandboxFunction>? _functionLookup;
    private SandboxNodeMap? _debugNodeMap;

    public ExecutionPlan(
        string moduleHash,
        string planHash,
        ExecutionPlanSeal planSeal,
        string policyHash,
        string bindingManifestHash,
        SandboxModule module,
        SandboxPolicy policy,
        BindingRegistry bindings,
        ResourceLimits budget,
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? bindingReferences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHash);
        ArgumentNullException.ThrowIfNull(planSeal);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingManifestHash);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(budget);

        ModuleHash = moduleHash;
        PlanHash = planHash;
        PlanSeal = planSeal;
        PolicyHash = policyHash;
        BindingManifestHash = bindingManifestHash;
        Module = module;
        Policy = policy;
        Bindings = bindings;
        Budget = budget;
        FunctionAnalysis = CopyFunctionAnalysis(functionAnalysis);
        BindingReferences = CopyBindingReferences(bindingReferences ?? BindingReferenceCollector.CollectByFunction(module, bindings));
    }

    public string ModuleHash { get; }
    public string PlanHash { get; }
    public ExecutionPlanSeal PlanSeal { get; }
    public string PolicyHash { get; }
    public string BindingManifestHash { get; }
    public SandboxModule Module { get; }
    public SandboxPolicy Policy { get; }
    public BindingRegistry Bindings { get; }
    public ResourceLimits Budget { get; }
    public IReadOnlyDictionary<string, FunctionAnalysis> FunctionAnalysis { get; }
    public IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences { get; }

    internal SandboxNodeMap DebugNodeMap
        => Volatile.Read(ref _debugNodeMap) ?? BuildDebugNodeMap();

    // The module function set is immutable for the lifetime of a prepared plan, so the id->function
    // index is built once and reused across every interpreted run instead of being rebuilt per
    // execution. Lazy + Volatile keeps construction race-free without locking on the hot path.
    public IReadOnlyDictionary<string, SandboxFunction> FunctionLookup
        => Volatile.Read(ref _functionLookup) ?? BuildFunctionLookup();

    internal ExecutionPlanEntrypointMetadata GetEntrypointMetadata(string entrypoint)
    {
        var lookup = Volatile.Read(ref _entrypointMetadataLookup) ?? BuildEntrypointMetadataLookup();
        return lookup.TryGetValue(entrypoint, out var metadata)
            ? metadata
            : Volatile.Read(ref _moduleOnlyEntrypointMetadata)!;
    }

    private FrozenDictionary<string, SandboxFunction> BuildFunctionLookup()
    {
        var lookup = Module.Functions.ToFrozenDictionary(f => f.Id, StringComparer.Ordinal);
        Volatile.Write(ref _functionLookup, lookup);
        return lookup;
    }

    private SandboxNodeMap BuildDebugNodeMap()
    {
        var map = SandboxNodeMap.Create(Module);
        Interlocked.CompareExchange(ref _debugNodeMap, map, null);
        return _debugNodeMap;
    }

    private FrozenDictionary<string, ExecutionPlanEntrypointMetadata> BuildEntrypointMetadataLookup()
    {
        var moduleCapabilities = ModuleCapabilityIds();
        Volatile.Write(
            ref _moduleOnlyEntrypointMetadata,
            new ExecutionPlanEntrypointMetadata(moduleCapabilities, hasAsyncBinding: false, hasHostBinding: false));

        var metadata = new Dictionary<string, ExecutionPlanEntrypointMetadata>(
            BindingReferences.Count,
            StringComparer.Ordinal);
        foreach (var pair in BindingReferences)
        {
            metadata[pair.Key] = BuildEntrypointMetadata(moduleCapabilities, pair.Value);
        }

        var lookup = metadata.ToFrozenDictionary(StringComparer.Ordinal);
        Volatile.Write(ref _entrypointMetadataLookup, lookup);
        return lookup;
    }

    private string[] ModuleCapabilityIds()
    {
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in Module.CapabilityRequests)
        {
            capabilities.Add(request.Id);
        }

        return capabilities.Count == 0 ? [] : capabilities.ToArray();
    }

    private ExecutionPlanEntrypointMetadata BuildEntrypointMetadata(
        string[] moduleCapabilities,
        IReadOnlySet<string> bindingReferences)
    {
        if (bindingReferences.Count == 0)
        {
            return new ExecutionPlanEntrypointMetadata(
                moduleCapabilities,
                hasAsyncBinding: false,
                hasHostBinding: false);
        }

        var required = new HashSet<string>(moduleCapabilities, StringComparer.Ordinal);
        var hasAsyncBinding = false;
        foreach (var bindingId in bindingReferences)
        {
            if (!Bindings.TryGet(bindingId, out var binding))
            {
                continue;
            }

            if (binding.RequiredCapability is not null)
            {
                required.Add(binding.RequiredCapability);
            }

            if (binding.IsAsync)
            {
                hasAsyncBinding = true;
            }

            if (binding.IsAsync || (binding.Effects & SandboxEffect.Concurrency) != 0)
            {
                required.Add(RuntimeCapabilityIds.Async);
            }
        }

        return new ExecutionPlanEntrypointMetadata(
            required.Count == 0 ? [] : required.ToArray(),
            hasAsyncBinding,
            hasHostBinding: true);
    }

    private static IReadOnlyDictionary<string, FunctionAnalysis> CopyFunctionAnalysis(
        IReadOnlyDictionary<string, FunctionAnalysis> functionAnalysis)
    {
        ArgumentNullException.ThrowIfNull(functionAnalysis);
        var copy = new Dictionary<string, FunctionAnalysis>(functionAnalysis.Count, StringComparer.Ordinal);
        foreach (var item in functionAnalysis)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                throw new ArgumentException("Function analysis keys cannot be null or whitespace.", nameof(functionAnalysis));
            }
            if (item.Value is null)
            {
                throw new ArgumentException("Function analysis entries cannot be null.", nameof(functionAnalysis));
            }
            if (item.Value.ReturnType is null)
            {
                throw new ArgumentException("Function analysis entries must declare a return type.", nameof(functionAnalysis));
            }
            if (!item.Value.Effects.ContainsOnlyKnownBits())
            {
                throw new ArgumentException(
                    "Function analysis entries must contain only known effect bits.",
                    nameof(functionAnalysis));
            }
            copy.Add(item.Key, item.Value);
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, FunctionAnalysis>(copy);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
    {
        ArgumentNullException.ThrowIfNull(bindingReferences);
        var copy = new Dictionary<string, IReadOnlySet<string>>(bindingReferences.Count, StringComparer.Ordinal);
        foreach (var item in bindingReferences)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                throw new ArgumentException("Binding reference keys cannot be null or whitespace.", nameof(bindingReferences));
            }
            if (item.Value is null)
            {
                throw new ArgumentException("Binding reference sets cannot be null.", nameof(bindingReferences));
            }
            if (item.Value.Contains(null!))
            {
                throw new ArgumentException("Binding reference entries cannot be null.", nameof(bindingReferences));
            }
            if (item.Value.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Binding reference entries cannot be null or whitespace.",
                    nameof(bindingReferences));
            }
            copy.Add(item.Key, item.Value.ToFrozenSet(StringComparer.Ordinal));
        }

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlySet<string>>(copy);
    }
}

public sealed class ExecutionPlanSeal : IEquatable<ExecutionPlanSeal>
{
    private readonly string _value;

    public ExecutionPlanSeal(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public bool Equals(ExecutionPlanSeal? other)
        => other is not null && StringComparer.Ordinal.Equals(_value, other._value);

    public override bool Equals(object? obj) => Equals(obj as ExecutionPlanSeal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    public override string ToString() => "[redacted]";
}

public sealed record SandboxExecutionOptions
{
    public ExecutionMode Mode { get; init; } = ExecutionMode.Auto;
    public SandboxIsolation Isolation { get; init; } = SandboxIsolation.InProcess;
    public bool EnableDebugTrace { get; init; }
    public bool AllowFallbackToInterpreter { get; init; } = true;
    public bool RequireDeterministic { get; init; }
    public SandboxRunId? RunId { get; init; }
    public int AutoCompileThreshold { get; init; } = 20;
    public ISandboxExecutionDebugHook? DebugHook { get; init; }

    internal bool RequiresInterpreter => EnableDebugTrace || DebugHook is not null;

    /// <summary>
    /// Drops the successful-run <c>RunSummary</c> audit event to avoid its allocation on the hot
    /// no-binding plugin dispatch path. Failure runs always emit their summary regardless. This is
    /// an <b>in-process-only</b> optimization: worker-isolated execution clears it (see
    /// <c>SandboxWorkerExecutor</c>) because worker-result audit validation requires the summary.
    /// Internal because suppressing audit on success is never a supported external contract.
    /// </summary>
    internal bool SuppressSuccessfulRunSummaryAudit { get; init; }
}

public enum ExecutionMode
{
    Interpreted,
    Compiled,
    Auto
}

public enum SandboxIsolation
{
    InProcess,
    WorkerProcess
}
