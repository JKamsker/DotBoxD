using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using System.Globalization;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;

internal static class WorkerAuditValidator
{
    private static readonly DateTimeOffset EarliestAcceptedTimestamp = DateTimeOffset.UnixEpoch;
    private static readonly Dictionary<string, AuditKindValidator> AuditKindValidators = new(StringComparer.Ordinal)
    {
        ["RunSummary"] = static (plan, _, _, auditEvent) => RunSummarySchemaMatches(plan, auditEvent),
        ["WorkerExecution"] = static (plan, _, _, auditEvent) => ModuleAuditMatches(plan, auditEvent),
        ["ExecutionFallback"] = static (plan, _, _, auditEvent) => ExecutionFallbackAuditMatches(plan, auditEvent),
        ["VerifierFailure"] = static (plan, _, _, auditEvent) => VerifierFailureAuditMatches(plan, auditEvent),
        ["DebugTrace"] = static (plan, _, options, auditEvent) =>
            options.EnableDebugTrace && ModuleAuditMatches(plan, auditEvent),
        ["BindingCall"] = static (plan, entrypoint, _, auditEvent) => BindingAuditMatches(plan, entrypoint, auditEvent),
        ["SandboxLog"] = static (plan, entrypoint, _, auditEvent) => BindingAuditMatches(plan, entrypoint, auditEvent),
        ["PluginMessage"] = static (plan, entrypoint, _, auditEvent) => BindingAuditMatches(plan, entrypoint, auditEvent),
    };

    private static readonly HashSet<string> CommonRunSummaryFields = [
        "mode",
        "executionMode",
        "executionDispatched",
        "cacheStatus",
        "moduleHash",
        "planHash",
        "policyId",
        "policyHash",
        "bindingManifestHash",
        "fuelUsed",
        "maxFuel",
        "loopIterations",
        "maxLoopIterations",
        "allocatedBytes",
        "allocationCharged",
        "maxAllocatedBytes",
        "hostCalls",
        "maxHostCalls",
        "fileBytesRead",
        "maxFileBytesRead",
        "fileBytesWritten",
        "maxFileBytesWritten",
        "networkBytesRead",
        "maxNetworkBytesRead",
        "networkBytesWritten",
        "maxNetworkBytesWritten",
        "logEvents",
        "maxLogEvents",
        "collectionElements",
        "maxCollectionElements",
        "stringBytes",
        "maxStringBytes"
    ];

    private delegate bool AuditKindValidator(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent);

    public static bool Matches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxAuditEvent auditEvent)
    {
        if (!CommonEnvelopeMatches(plan, auditEvent))
        {
            return false;
        }

        return AuditKindValidators.TryGetValue(auditEvent.Kind, out var validate) &&
            validate(plan, entrypoint, options, auditEvent);
    }

    private static bool CommonEnvelopeMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (string.IsNullOrWhiteSpace(auditEvent.Kind) ||
            !TextIsSafe(auditEvent.Kind) ||
            !TextIsSafe(auditEvent.ResourceId) ||
            !TextIsSafe(auditEvent.Message) ||
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

    private static bool ResultShapeMatches(SandboxAuditEvent auditEvent)
        => !auditEvent.Success ||
           auditEvent.ErrorCode is null ||
           auditEvent.Kind == "ExecutionFallback";

    private static bool TimestampMatches(ExecutionPlan plan, DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero || timestamp < EarliestAcceptedTimestamp)
        {
            return false;
        }

        if (plan.Policy.Deterministic && plan.Policy.LogicalNow is { } logicalNow)
        {
            return timestamp == logicalNow;
        }

        return timestamp <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool RunSummarySchemaMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (!RunSummaryEnvelopeMatches(plan, auditEvent))
        {
            return false;
        }

        foreach (var field in auditEvent.Fields!)
        {
            if (!RunSummaryFieldMatches(plan, field))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RunSummaryEnvelopeMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.Fields is not null &&
           string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal);

    private static bool RunSummaryFieldMatches(ExecutionPlan plan, KeyValuePair<string, string> field)
        => FieldNameAllowed(plan, field.Key) &&
           !string.IsNullOrWhiteSpace(field.Key) &&
           TextIsSafe(field.Key) &&
           TextIsSafe(field.Value);

    private static bool FieldNameAllowed(ExecutionPlan plan, string key)
        => CommonRunSummaryFields.Contains(key) ||
           (plan.Policy.Deterministic && key == "logicalNow") ||
           key is "runtimeForm" or "cacheKey" or "artifactHash" or "materializationStatus";

    private static bool ModuleAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None &&
           auditEvent.Fields is null &&
           string.Equals(auditEvent.ResourceId, $"module:{plan.ModuleHash}", StringComparison.Ordinal);

    private static bool ExecutionFallbackAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => auditEvent.Success &&
           auditEvent.ErrorCode is SandboxErrorCode.ValidationError or SandboxErrorCode.VerifierFailure &&
           ModuleAuditMatches(plan, auditEvent);

    private static bool VerifierFailureAuditMatches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
        => !auditEvent.Success &&
           auditEvent.ErrorCode == SandboxErrorCode.VerifierFailure &&
           ModuleAuditMatches(plan, auditEvent);

    private static bool BindingAuditMatches(
        ExecutionPlan plan,
        string entrypoint,
        SandboxAuditEvent auditEvent)
    {
        if (!TryGetAuditedBinding(plan, entrypoint, auditEvent, out var binding))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(auditEvent.ResourceId) &&
            CapabilityMatches(auditEvent, binding) &&
            EffectMatches(auditEvent, binding) &&
            ResultMatches(auditEvent) &&
            LogAuditMatchesPolicy(plan, auditEvent) &&
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
        return binding.AuditLevel is not (AuditLevel.None or AuditLevel.Summary);
    }

    private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingSignature binding)
        => binding.RequiredCapability is null ||
           string.Equals(auditEvent.CapabilityId, binding.RequiredCapability, StringComparison.Ordinal);

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
        => auditEvent.Kind != "SandboxLog" ||
           auditEvent.Message is not null &&
           auditEvent.Message.Length <= plan.Budget.MaxLogMessageLength;

    private static bool RequiredBindingFieldsMatch(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (!RequiredBindingFieldValuesMatch(plan, auditEvent, out var durationMs))
        {
            return false;
        }

        return FieldsAreSafe(auditEvent.Fields!) &&
            NonNegativeDuration(durationMs);
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
                !TextIsSafe(field.Key) ||
                !TextIsSafe(field.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NonNegativeDuration(string durationMs)
        => double.TryParse(
            durationMs,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedDuration) &&
            parsedDuration >= 0;

    private static bool TextIsSafe(string? value)
        => value is null ||
           string.Equals(AuditTextSanitizer.SanitizeAndRedact(value), value, StringComparison.Ordinal);
}
