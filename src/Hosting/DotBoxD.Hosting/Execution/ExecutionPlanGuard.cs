using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Hosting.Execution;

internal static class ExecutionPlanGuard
{
    public static void EnsurePolicyLimits(SandboxPolicy policy)
    {
        if (policy.ResourceLimits is null)
        {
            throw new SandboxValidationException([MissingResourceLimitsDiagnostic()]);
        }

        try
        {
            ResourceLimitValidation.Validate(policy.ResourceLimits);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new SandboxValidationException([
                CreatePolicyLimitDiagnostic(ex)
            ]);
        }
    }

    public static void EnsurePrepared(
        ExecutionPlan plan,
        BindingRegistry hostBindings,
        byte[] planSigningKey,
        PreparedPlanIntegrityCache preparedPlans)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ReferenceEquals(plan.Bindings, hostBindings) &&
            preparedPlans.ContainsTrustedReference(plan))
        {
            return;
        }

        if (IsTrustedPreparedPlan(plan, hostBindings, preparedPlans))
        {
            // This host prepared, validated, and sealed this exact identity; the per-run check is
            // O(1) against the trusted entry instead of rebuilding the whole prepare pipeline.
            return;
        }

        var diagnostics = new List<SandboxDiagnostic>();
        EnsurePolicyLimits(plan.Policy, diagnostics);
        if (!ReferenceEquals(plan.Bindings, hostBindings))
        {
            diagnostics.Add(new SandboxDiagnostic("E-PLAN-BINDINGS", "execution plan was not prepared by this host"));
        }

        var validation = new ModuleValidator().Validate(plan.Module, hostBindings, plan.Policy);
        if (!validation.Succeeded)
        {
            diagnostics.AddRange(validation.Diagnostics);
        }
        else
        {
            var expected = ExecutionPlanBuilder.Build(
                plan.Module,
                plan.Policy,
                hostBindings,
                validation.Functions,
                validation.BindingReferences,
                planSigningKey);
            ComparePlan(plan, expected, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            throw new SandboxValidationException(diagnostics);
        }
    }

    private static void EnsurePolicyLimits(SandboxPolicy policy, List<SandboxDiagnostic> diagnostics)
    {
        if (policy.ResourceLimits is null)
        {
            diagnostics.Add(MissingResourceLimitsDiagnostic());
            return;
        }

        try
        {
            ResourceLimitValidation.Validate(policy.ResourceLimits);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            diagnostics.Add(CreatePolicyLimitDiagnostic(ex));
        }
    }

    private static SandboxDiagnostic MissingResourceLimitsDiagnostic()
        => new("E-POLICY-LIMIT", "policy resource limits are required");

    private static SandboxDiagnostic CreatePolicyLimitDiagnostic(ArgumentOutOfRangeException ex)
    {
        var reason = ResourceLimitValidationReasons.GetReason(ex) ?? "must be non-negative";
        return new SandboxDiagnostic("E-POLICY-LIMIT", $"policy resource limit '{ex.ParamName}' {reason}");
    }

    private static bool IsTrustedPreparedPlan(
        ExecutionPlan plan,
        BindingRegistry hostBindings,
        PreparedPlanIntegrityCache preparedPlans)
        => ReferenceEquals(plan.Bindings, hostBindings) &&
           preparedPlans.TryGetTrusted(plan.PlanSeal, out var trusted) &&
           MatchesTrustedIdentity(plan, trusted);

    // Copies must retain the validated module, policy, and binding instances. Their metadata uses
    // the same comparison as a rebuilt plan, so cached and full validation agree.
    private static bool MatchesTrustedIdentity(ExecutionPlan candidate, ExecutionPlan trusted)
        => ReferenceEquals(candidate, trusted) ||
           (ReferenceEquals(candidate.Module, trusted.Module) &&
            ReferenceEquals(candidate.Policy, trusted.Policy) &&
            ReferenceEquals(candidate.Bindings, trusted.Bindings) &&
            SamePreparedMetadata(candidate, trusted));

    private static bool SamePreparedMetadata(ExecutionPlan candidate, ExecutionPlan expected)
        => candidate.ModuleHash == expected.ModuleHash &&
           candidate.PolicyHash == expected.PolicyHash &&
           candidate.BindingManifestHash == expected.BindingManifestHash &&
           candidate.PlanHash == expected.PlanHash &&
           candidate.PlanSeal.Equals(expected.PlanSeal) &&
           candidate.Budget == expected.Budget &&
           SameAnalysis(candidate.FunctionAnalysis, expected.FunctionAnalysis) &&
           SameBindingReferences(candidate.BindingReferences, expected.BindingReferences);

    private static void ComparePlan(
        ExecutionPlan plan,
        ExecutionPlan expected,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!SamePreparedMetadata(plan, expected))
        {
            diagnostics.Add(new SandboxDiagnostic("E-PLAN-INTEGRITY", "execution plan does not match validated module, policy, and bindings"));
        }
    }

    private static bool SameAnalysis(
        IReadOnlyDictionary<string, FunctionAnalysis> left,
        IReadOnlyDictionary<string, FunctionAnalysis> right)
        => left.Count == right.Count &&
           left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);

    private static bool SameBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> left,
        IReadOnlyDictionary<string, IReadOnlySet<string>> right)
        => left.Count == right.Count &&
           left.All(item =>
               right.TryGetValue(item.Key, out var value) &&
               item.Value.SetEquals(value));
}
