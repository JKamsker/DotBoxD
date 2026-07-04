using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

internal static class WorkerPluginMessageAuditPolicy
{
    private const string PluginMessageKind = "PluginMessage";
    private const string SendBindingId = "host.message.send";
    private const string CapabilityId = "host.message.write";
    private const string TargetPrefix = "target:";

    public static bool Matches(ExecutionPlan plan, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.Kind != PluginMessageKind)
        {
            return true;
        }

        if (!string.Equals(auditEvent.BindingId, SendBindingId, StringComparison.Ordinal) ||
            !string.Equals(auditEvent.CapabilityId, CapabilityId, StringComparison.Ordinal) ||
            !TryReadTargetId(auditEvent.ResourceId, out var targetId) ||
            !plan.Policy.TryGetGrant(CapabilityId, out var grant))
        {
            return false;
        }

        return TargetMatches(grant, targetId) &&
            MessageLengthMatches(grant, auditEvent);
    }

    private static bool TryReadTargetId(string? resourceId, out string targetId)
    {
        targetId = "";
        if (resourceId is null ||
            !resourceId.StartsWith(TargetPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        targetId = resourceId[TargetPrefix.Length..];
        return SandboxLiteralConstraints.IsOpaqueId(targetId);
    }

    private static bool TargetMatches(CapabilityGrant grant, string targetId)
    {
        var allowedTargets = ReadCsv(grant, "allowedTargets");
        var targetPrefixes = ReadCsv(grant, "targetPrefixes");
        if (allowedTargets is null && targetPrefixes is null)
        {
            return true;
        }

        if (allowedTargets is not null && allowedTargets.Contains(targetId))
        {
            return true;
        }

        if (targetPrefixes is null)
        {
            return false;
        }

        foreach (var prefix in targetPrefixes)
        {
            if (targetId.StartsWith(prefix, StringComparison.Ordinal) &&
                (targetId.Length == prefix.Length ||
                 prefix[^1] is '.' or ':' ||
                 targetId[prefix.Length] is '.' or ':'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MessageLengthMatches(CapabilityGrant grant, SandboxAuditEvent auditEvent)
    {
        if (!grant.Parameters.TryGetValue("maxMessageLength", out var value))
        {
            return true;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxMessageLength) &&
            maxMessageLength >= 0 &&
            TryReadMessageLength(auditEvent, out var messageLength) &&
            messageLength <= maxMessageLength;
    }

    private static bool TryReadMessageLength(SandboxAuditEvent auditEvent, out int messageLength)
    {
        messageLength = 0;
        return auditEvent.Fields is not null &&
            auditEvent.Fields.TryGetValue("messageLength", out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out messageLength) &&
            messageLength >= 0;
    }

    private static IReadOnlySet<string>? ReadCsv(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value)
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal)
            : null;
}
