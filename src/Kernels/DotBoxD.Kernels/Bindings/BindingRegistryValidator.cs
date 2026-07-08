using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingRegistryValidator
{
    internal static readonly HashSet<string> BuiltInCapabilities = new(StringComparer.Ordinal) {
        "file.read", "file.write", "time.now", "random", "log.write"
    };

    private static readonly IReadOnlyDictionary<string, SandboxEffect> BuiltInCapabilityEffects =
        new Dictionary<string, SandboxEffect>(StringComparer.Ordinal)
        {
            ["file.read"] = SandboxEffect.FileRead,
            ["file.write"] = SandboxEffect.FileWrite,
            ["time.now"] = SandboxEffect.Time,
            ["random"] = SandboxEffect.Random,
            ["log.write"] = SandboxEffect.Audit
        };

    public static IReadOnlyList<SandboxDiagnostic> Validate(IReadOnlyList<BindingDescriptor> bindings)
    {
        var diagnostics = new List<SandboxDiagnostic>();
        CheckDuplicateBindingIds(bindings, diagnostics);

        foreach (var binding in bindings)
        {
            ValidateBinding(binding, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateBinding(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        if (!BindingDescriptorRequiredFieldValidator.Validate(binding, diagnostics))
        {
            return;
        }

        BindingValidationPhases.ValidateBindingIdentity(binding, diagnostics);
        BindingValidationPhases.ValidateBindingEffectBits(binding, diagnostics);
        BindingValidationPhases.ValidateBindingClassifications(binding, diagnostics);
        BindingValidationPhases.ValidateCapabilityRequirements(binding, diagnostics);
        BindingValidationPhases.ValidateSandboxReach(binding, diagnostics);
        BindingValidationPhases.ValidateCustomCapabilityGrant(binding, diagnostics);
        ValidateBuiltInCapabilityEffect(binding, diagnostics);
        BindingValidationPhases.ValidateDangerousBinding(binding, diagnostics);
        ValidateCostModel(binding, diagnostics);
        BindingCompiledTargetValidator.Validate(binding, diagnostics);
        BindingValidationPhases.ValidateBindingTypes(binding, diagnostics);
    }

    private static void CheckDuplicateBindingIds(
        IReadOnlyList<BindingDescriptor> bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        if (bindings.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(bindings.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < bindings.Count; i++)
        {
            IncrementCount(counts, bindings[i].Id, ref nullCount);
        }

        var reportedNull = false;
        for (var i = 0; i < bindings.Count; i++)
        {
            var id = bindings[i].Id;
            if (ShouldReportDuplicate(counts, id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic("E-BINDING-DUP", $"duplicate binding id '{id}'"));
            }
        }
    }

    private static void IncrementCount(Dictionary<string, int> counts, string? value, ref int nullCount)
    {
        if (value is null)
        {
            nullCount++;
            return;
        }

        counts.TryGetValue(value, out var count);
        counts[value] = count + 1;
    }

    private static bool ShouldReportDuplicate(
        Dictionary<string, int> counts,
        string? value,
        int nullCount,
        ref bool reportedNull)
    {
        if (value is null)
        {
            if (nullCount < 2 || reportedNull)
            {
                return false;
            }

            reportedNull = true;
            return true;
        }

        if (!counts.TryGetValue(value, out var count) || count < 2)
        {
            return false;
        }

        counts[value] = 0;
        return true;
    }

    internal static void ValidateType(
        BindingDescriptor binding,
        SandboxType type,
        List<SandboxDiagnostic> diagnostics)
        => BindingTypeChecks.Validate(binding.Id, type, diagnostics);

    private static void ValidateCostModel(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
    {
        var cost = binding.CostModel;
        if (cost.BaseFuel < 0 || cost.PerByteFuel < 0 || cost.MaxCallsPerRun is < 0)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COST", $"binding '{binding.Id}' declares a negative resource cost or call limit"));
        }
    }

    internal static void ValidateIdentifier(
        string value,
        string description,
        string code,
        List<SandboxDiagnostic> diagnostics)
    {
        if (BindingIdentifierValidator.TryValidate(value, out var message))
        {
            return;
        }

        diagnostics.Add(new SandboxDiagnostic(code, $"{description} {message}"));
    }

    internal static bool ReachesOutsideSandbox(BindingDescriptor binding)
        => IsExternal(binding.Safety) || binding.Effects.RequiresCapability();

    private static bool IsExternal(BindingSafety safety)
        => safety is BindingSafety.ReadOnlyExternal or BindingSafety.SideEffectingExternal;

    private static void ValidateBuiltInCapabilityEffect(
        BindingDescriptor binding,
        List<SandboxDiagnostic> diagnostics)
    {
        if (binding.RequiredCapability is null ||
            !BuiltInCapabilityEffects.TryGetValue(binding.RequiredCapability, out var requiredEffect))
        {
            return;
        }

        var allowedEffects = SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.Audit | requiredEffect;
        if ((binding.Effects & requiredEffect) == SandboxEffect.None ||
            (binding.Effects & ~allowedEffects) != SandboxEffect.None)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-CAP-EFFECT",
                $"binding '{binding.Id}' uses built-in capability '{binding.RequiredCapability}' with incompatible effects"));
        }
    }

}
