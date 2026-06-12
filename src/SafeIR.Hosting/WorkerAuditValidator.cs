namespace SafeIR.Hosting;

using SafeIR;

internal static class WorkerAuditValidator
{
    public static bool Matches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.Kind) ||
            !TextIsSafe(auditEvent.Kind) ||
            !TextIsSafe(auditEvent.ResourceId) ||
            !TextIsSafe(auditEvent.Message) ||
            auditEvent.Bytes is < 0 ||
            (auditEvent.ErrorCode is { } code && !Enum.IsDefined(code)) ||
            (auditEvent.Success && auditEvent.ErrorCode is not null))
        {
            return false;
        }

        return auditEvent.Kind switch
        {
            "RunSummary" => true,
            "WorkerExecution" => ModuleAuditMatches(plan, auditEvent),
            "DebugTrace" => options.EnableDebugTrace && ModuleAuditMatches(plan, auditEvent),
            "CacheInvalidated" => auditEvent.Success && ModuleAuditMatches(plan, auditEvent),
            "PolicyDenied" => PolicyDeniedAuditMatches(auditEvent),
            "BindingCall" or "SandboxLog" or "PluginMessage" => BindingAuditMatches(plan, entrypoint, auditEvent),
            _ => false
        };
    }

    private static bool ModuleAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.Fields is null &&
           string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal);

    private static bool PolicyDeniedAuditMatches(SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.BindingId is null &&
           !string.IsNullOrWhiteSpace(auditEvent.CapabilityId) &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.ErrorCode == SandboxErrorCode.PolicyDenied &&
           string.Equals(auditEvent.ResourceId, $"capability:{auditEvent.CapabilityId}", StringComparison.Ordinal);

    private static bool BindingAuditMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.BindingId) ||
            !plan.BindingReferences.TryGetValue(entrypoint, out var referencedBindings) ||
            !referencedBindings.Contains(auditEvent.BindingId) ||
            !plan.Bindings.TryGet(auditEvent.BindingId, out var binding) ||
            !CapabilityMatches(auditEvent, binding) ||
            !EffectMatches(auditEvent, binding) ||
            string.IsNullOrWhiteSpace(auditEvent.ResourceId) ||
            auditEvent.Fields is null ||
            !RequiredBindingFieldsMatch(auditEvent, plan.ModuleHash, plan.PolicyHash) ||
            (!auditEvent.Success && auditEvent.ErrorCode is null))
        {
            return false;
        }

        return auditEvent.Kind switch
        {
            "SandboxLog" => auditEvent.BindingId == "log.write",
            "PluginMessage" => auditEvent.BindingId == "game.message.send",
            "BindingCall" => auditEvent.BindingId is not "log.write" and not "game.message.send",
            _ => false
        };
    }

    private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingSignature binding)
        => binding.RequiredCapability is null
            ? auditEvent.CapabilityId is null
            : string.Equals(auditEvent.CapabilityId, binding.RequiredCapability, StringComparison.Ordinal);

    private static bool EffectMatches(SandboxAuditEvent auditEvent, BindingSignature binding)
    {
        if (auditEvent.Effect == SandboxEffect.None ||
            (auditEvent.Effect & ~binding.Effects) != SandboxEffect.None)
        {
            return false;
        }

        var nonCpuEffects = binding.Effects & ~SandboxEffect.Cpu;
        return nonCpuEffects == SandboxEffect.None ||
               (auditEvent.Effect & nonCpuEffects) != SandboxEffect.None;
    }

    private static bool RequiredBindingFieldsMatch(
        SandboxAuditEvent auditEvent,
        string moduleHash,
        string policyHash)
    {
        if (!auditEvent.Fields!.TryGetValue("resourceKind", out var resourceKind) ||
            string.IsNullOrWhiteSpace(resourceKind) ||
            !TextIsSafe(resourceKind) ||
            !auditEvent.Fields.TryGetValue("durationMs", out var durationMs) ||
            !auditEvent.Fields.TryGetValue("moduleHash", out var auditModuleHash) ||
            !string.Equals(auditModuleHash, moduleHash, StringComparison.Ordinal) ||
            !auditEvent.Fields.TryGetValue("policyHash", out var auditPolicyHash) ||
            !string.Equals(auditPolicyHash, policyHash, StringComparison.Ordinal))
        {
            return false;
        }

        return double.TryParse(
                durationMs,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) &&
            parsed >= 0;
    }

    private static bool TextIsSafe(string? value)
        => value is null || value.All(c => !char.IsControl(c));
}
