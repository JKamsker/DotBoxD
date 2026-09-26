using System.Runtime.CompilerServices;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Plugins.Inspection;

public sealed record CapabilityExplanation(string Capability, bool Granted, string Explanation);
public sealed record LoweredOperation(string Function, string Operation, SourceSpan Span);
public sealed record KernelInspection(
    string ModuleId, bool Valid, IReadOnlyList<SandboxDiagnostic> Diagnostics,
    IReadOnlyList<CapabilityExplanation> Capabilities, IReadOnlyList<BindingSignature> Bindings,
    ResourceLimits Limits, IReadOnlyList<LoweredOperation> Operations, string LoweredJson);
public sealed record ExecutionExplanation(ExecutionMode SelectedMode, bool CompiledCandidate, string Reason);

/// <summary>Read-only inspection over public validation, IR and execution-selection primitives.</summary>
public static class PluginInspection
{
    public static KernelInspection Explain(SandboxModule module, IBindingCatalog bindings, SandboxPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(policy);
        var validation = new ModuleValidator().Validate(module, bindings, policy);
        var now = policy.GrantClock;
        var capabilities = validation.RequiredCapabilities.Order(StringComparer.Ordinal).Select(id =>
        {
            var granted = policy.GrantsCapability(id, now);
            return new CapabilityExplanation(id, granted, granted
                ? "An active host policy grant permits this capability; parameter constraints still apply."
                : $"Rejected because {id} has no active host policy grant.");
        }).ToArray();
        var referenced = validation.BindingReferences.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);
        var reachedBindings = bindings.Signatures.Where(binding => referenced.Contains(binding.Id))
            .OrderBy(binding => binding.Id, StringComparer.Ordinal).ToArray();
        var operations = LoweredOperationCollector.Collect(module);
        return new KernelInspection(module.Id, validation.Succeeded, validation.Diagnostics,
            Array.AsReadOnly(capabilities), Array.AsReadOnly(reachedBindings), policy.ResourceLimits,
            Array.AsReadOnly(operations), JsonExporter.Export(module, indented: true));
    }

    /// <summary>
    /// Explains selection for the built-in hotness selector. CompiledCandidate is a preflight;
    /// compilation and verification may still reject a candidate. Custom selectors own their decisions.
    /// </summary>
    public static ExecutionExplanation ExplainExecution(
        ExecutionPlan plan, string entrypoint, SandboxExecutionOptions options,
        int runCount = 1, bool compilerConfigured = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode) || runCount < 1)
        {
            throw new ArgumentException("Expected a defined execution mode and positive run count.");
        }
        if (!plan.FunctionLookup.TryGetValue(entrypoint, out var function) || !function.IsEntrypoint)
        {
            throw new ArgumentException("The requested entrypoint is unavailable.", nameof(entrypoint));
        }
        var asyncBinding = plan.BindingReferences.TryGetValue(entrypoint, out var ids) &&
            ids.Any(id => plan.Bindings.TryGet(id, out var binding) && binding.IsAsync);
        var fallbackReason = InterpreterReason(compilerConfigured, asyncBinding, options.EnableDebugTrace);
        var candidate = fallbackReason is null;
        if (options.Mode != ExecutionMode.Auto)
        {
            return new(options.Mode, candidate, "The caller explicitly selected this backend; its validation still applies.");
        }
        if (!candidate)
        {
            return new(ExecutionMode.Interpreted, false, fallbackReason!);
        }
        var selected = new HotnessExecutionModeSelector().Choose(plan, options,
            new ModuleHotnessStats(runCount), CompiledCacheStatus.None).Mode;
        return new(selected, true, selected == ExecutionMode.Interpreted
            ? "Auto is warming up before the configured compilation threshold."
            : "Auto reached the compilation threshold; compilation and verification must succeed before execution.");
    }
    private static string? InterpreterReason(bool compilerConfigured, bool asyncBinding, bool debugTrace)
    {
        if (!compilerConfigured || !RuntimeFeature.IsDynamicCodeSupported)
        {
            return "Auto uses the interpreter because dynamic compilation is unavailable.";
        }
        if (asyncBinding)
        {
            return "Auto uses the interpreter because a reachable binding is asynchronous.";
        }
        return debugTrace ? "Auto uses the interpreter because debug tracing is enabled." : null;
    }
}
