using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal static class WorkerFileAuditGrantValidator
{
    private const string FileRead = "file.read";
    private const string FileWrite = "file.write";

    public static bool Matches(ExecutionPlan plan, SandboxAuditEvent auditEvent, DateTimeOffset grantClock)
    {
        if (auditEvent.CapabilityId is not FileRead and not FileWrite)
        {
            return true;
        }

        if (!auditEvent.Success)
        {
            return true;
        }

        return plan.Policy.TryGetGrant(auditEvent.CapabilityId, grantClock, out var grant) &&
            grant.Parameters.TryGetValue("root", out var root) &&
            !string.IsNullOrWhiteSpace(root) &&
            ResourceMatches(auditEvent) &&
            GrantLimitsMatch(grant, auditEvent);
    }

    private static bool ResourceMatches(SandboxAuditEvent auditEvent)
    {
        var prefix = $"sandbox://{auditEvent.CapabilityId}/";
        if (auditEvent.ResourceId is not { } resource ||
            !resource.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var relativePath = resource[prefix.Length..];
        return SandboxLiteralConstraints.IsPortableRelativePath(relativePath);
    }

    private static bool GrantLimitsMatch(CapabilityGrant grant, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.Bytes is not { } bytes)
        {
            return false;
        }

        try
        {
            var options = SafeFileGrantReader.Read(grant);
            return BytesMatch(options, bytes) &&
                ExtensionMatches(options, auditEvent.ResourceId!);
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    private static bool BytesMatch(SafeFileGrantOptions options, long bytes)
        => options.MaxBytesPerRun is not { } maxBytes || bytes <= maxBytes;

    private static bool ExtensionMatches(SafeFileGrantOptions options, string resource)
    {
        if (options.AllowedExtensions is null)
        {
            return true;
        }

        var extension = Path.GetExtension(resource);
        return options.AllowedExtensions.Contains(extension);
    }
}
