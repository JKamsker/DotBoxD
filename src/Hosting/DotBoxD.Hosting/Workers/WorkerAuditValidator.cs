using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using System.Globalization;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime.Bindings;

internal static class WorkerAuditValidator
{
    private static readonly DateTimeOffset EarliestAcceptedTimestamp = DateTimeOffset.UnixEpoch;
    private static readonly Dictionary<string, AuditKindValidator> AuditKindValidators = new(StringComparer.Ordinal)
    {
        ["RunSummary"] = static (plan, _, _, auditEvent, _) => WorkerRunSummaryAuditValidator.Matches(plan, auditEvent),
        ["WorkerExecution"] = static (plan, _, _, auditEvent, _) => ModuleAuditMatches(plan, auditEvent),
        ["ExecutionFallback"] = static (plan, _, _, auditEvent, _) => ExecutionFallbackAuditMatches(plan, auditEvent),
        ["VerifierFailure"] = static (plan, _, _, auditEvent, _) => VerifierFailureAuditMatches(plan, auditEvent),
        ["DebugTrace"] = static (plan, _, options, auditEvent, _) =>
            options.EnableDebugTrace && ModuleAuditMatches(plan, auditEvent),
        ["CacheInvalidated"] = static (plan, entrypoint, _, auditEvent, _) =>
            WorkerCacheInvalidationAuditValidator.Matches(plan, entrypoint, auditEvent),
        ["PolicyDenied"] = static (_, _, _, _, _) => false,
        [BindingAuditKinds.BindingCall] = static (plan, entrypoint, _, auditEvent, grantClock) =>
            BindingAuditMatches(plan, entrypoint, auditEvent, grantClock, allowInProcessEvidence: false),
        [BindingAuditKinds.SandboxLog] = static (plan, entrypoint, _, auditEvent, grantClock) =>
            BindingAuditMatches(plan, entrypoint, auditEvent, grantClock, allowInProcessEvidence: false),
        [BindingAuditKinds.PluginMessage] = static (plan, entrypoint, _, auditEvent, grantClock) =>
            BindingAuditMatches(plan, entrypoint, auditEvent, grantClock, allowInProcessEvidence: false),
    };

    private delegate bool AuditKindValidator(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent,
        DateTimeOffset grantClock);

    public static bool Matches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent,
        DateTimeOffset grantClock)
    {
        if (!CommonEnvelopeMatches(plan, auditEvent))
        {
            return false;
        }

        return AuditKindValidators.TryGetValue(auditEvent.Kind, out var validate) &&
            validate(plan, entrypoint, options, auditEvent, grantClock);
    }

    internal static bool CommonEnvelopeMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.Kind) ||
            !WorkerAuditTextSafety.TextIsSafe(auditEvent.Kind) ||
            !WorkerAuditTextSafety.TextIsSafe(auditEvent.ResourceId) ||
            !WorkerAuditTextSafety.TextIsSafe(auditEvent.Message) ||
            auditEvent.Bytes is < 0)
        {
            return false;
        }

        if (auditEvent.ErrorCode is { } code && !Enum.IsDefined(code))
        {
            return false;
        }

        return ResultShapeMatches(auditEvent) &&
            TimestampMatches(plan, auditEvent.Timestamp);
    }

    internal static bool InterpreterBindingMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent,
        DateTimeOffset grantClock)
        => CommonEnvelopeMatches(plan, auditEvent) &&
           BindingAuditMatches(
               plan,
               entrypoint,
               auditEvent,
               grantClock,
               allowInProcessEvidence: true);

    private static bool ResultShapeMatches(SandboxAuditEvent auditEvent)
        => auditEvent.Success
            ? auditEvent.ErrorCode is null
            : auditEvent.ErrorCode is not null;

    private static bool TimestampMatches(ExecutionPlan plan, DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero || timestamp < EarliestAcceptedTimestamp)
        {
            return false;
        }

        if (plan.Policy.Deterministic)
        {
            return timestamp == (plan.Policy.LogicalNow ?? DateTimeOffset.UnixEpoch);
        }

        return timestamp <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool ModuleAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.Fields is null &&
           string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal);

    private static bool ExecutionFallbackAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.ErrorCode is SandboxErrorCode.ValidationError or SandboxErrorCode.VerifierFailure &&
           ModuleAuditMatches(plan, auditEvent);

    private static bool VerifierFailureAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.ErrorCode == SandboxErrorCode.VerifierFailure &&
           ModuleAuditMatches(plan, auditEvent);

    private static bool BindingAuditMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent,
        DateTimeOffset grantClock,
        bool allowInProcessEvidence)
    {
        if (!TryGetAuditedBinding(plan, entrypoint, auditEvent, out var binding))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(auditEvent.ResourceId) &&
            AuditKindMatchesBinding(auditEvent.Kind, binding) &&
            CapabilityMatches(auditEvent, binding) &&
            EffectMatches(auditEvent, binding) &&
            ResultMatches(auditEvent) &&
            LogAuditMatchesPolicy(plan, auditEvent) &&
            WorkerBindingAuditResourceValidator.Matches(
                plan,
                auditEvent,
                binding,
                grantClock,
                allowInProcessEvidence) &&
            WorkerPluginMessageAuditPolicy.Matches(
                plan,
                auditEvent,
                grantClock,
                allowInProcessEvidence) &&
            RequiredBindingFieldsMatch(plan, auditEvent);
    }

    private static bool TryGetAuditedBinding(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent,
        out BindingSignature binding)
    {
        binding = null!;
        if (string.IsNullOrWhiteSpace(auditEvent.BindingId) ||
            !plan.BindingReferences.TryGetValue(entrypoint, out var entrypointBindings) ||
            !entrypointBindings.Contains(auditEvent.BindingId) ||
            !plan.Bindings.TryGet(auditEvent.BindingId, out var resolved))
        {
            return false;
        }

        binding = resolved;
        return binding.AuditLevel != AuditLevel.None;
    }

    private static bool AuditKindMatchesBinding(string kind, BindingSignature binding)
        => string.Equals(kind, binding.AuditKind, StringComparison.Ordinal);

    private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingSignature binding)
        => string.Equals(auditEvent.CapabilityId, binding.RequiredCapability, StringComparison.Ordinal);

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

    private static bool ResultMatches(SandboxAuditEvent auditEvent)
        => auditEvent.Success ? auditEvent.ErrorCode is null : auditEvent.ErrorCode is not null;

    private static bool LogAuditMatchesPolicy(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.Kind != BindingAuditKinds.SandboxLog ||
           auditEvent.Message is not null &&
           auditEvent.Message.Length <= plan.Budget.MaxLogMessageLength;

    private static bool RequiredBindingFieldsMatch(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (!RequiredBindingFieldValuesMatch(plan, auditEvent, out var durationMs))
        {
            return false;
        }

        return DeterministicTimeBindingFieldsMatch(plan, auditEvent) &&
            FieldsAreSafe(auditEvent.Fields!) &&
            DurationMatchesPlan(plan, durationMs);
    }

    private static bool RequiredBindingFieldValuesMatch(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        out string durationMs)
    {
        durationMs = string.Empty;
        if (auditEvent.Fields is not { } fields ||
            !RequiredTextField(fields, "resourceKind", out _) ||
            !RequiredTextField(fields, "durationMs", out var parsedDuration) ||
            !RequiredFieldEquals(fields, "moduleHash", plan.ModuleHash) ||
            !RequiredFieldEquals(fields, "policyHash", plan.PolicyHash))
        {
            return false;
        }

        durationMs = parsedDuration;
        return true;
    }

    private static bool RequiredTextField(
        IReadOnlyDictionary<string, string> fields,
        string key,
        out string value)
    {
        if (!fields.TryGetValue(key, out var candidate) || string.IsNullOrWhiteSpace(candidate))
        {
            value = string.Empty;
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool RequiredFieldEquals(IReadOnlyDictionary<string, string> fields, string key, string expected)
        => fields.TryGetValue(key, out var value) &&
           string.Equals(value, expected, StringComparison.Ordinal);

    private static bool FieldsAreSafe(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) ||
                !WorkerAuditTextSafety.TextIsSafe(field.Key) ||
                !WorkerAuditTextSafety.TextIsSafe(field.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DeterministicTimeBindingFieldsMatch(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (!plan.Policy.Deterministic ||
            auditEvent.BindingId != SafeTimeBindingNames.NowUnixMillisId)
        {
            return true;
        }

        return plan.Policy.LogicalNow is { } logicalNow &&
               auditEvent.Fields!.TryGetValue(SafeTimeBindingNames.NowUnixMillisAuditField, out var unixMillis) &&
               long.TryParse(unixMillis, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
               value == logicalNow.ToUnixTimeMilliseconds();
    }

    private static bool DurationMatchesPlan(ExecutionPlan plan, string durationMs)
    {
        if (!double.TryParse(
            durationMs,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedDuration) ||
            !double.IsFinite(parsedDuration) ||
            parsedDuration < 0)
        {
            return false;
        }

        return !plan.Policy.Deterministic || parsedDuration == 0;
    }
}
