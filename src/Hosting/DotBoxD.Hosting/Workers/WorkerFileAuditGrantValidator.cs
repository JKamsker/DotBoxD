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
    private const string PathExtensionField = "pathExtension";
    private const string WriteDispositionField = "writeDisposition";
    private const string RedactedSegment = "[redacted]";

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
                ExtensionMatches(options, auditEvent) &&
                WriteDispositionMatches(options, auditEvent);
        }
        catch (SandboxRuntimeException)
        {
            return false;
        }
    }

    private static bool BytesMatch(SafeFileGrantOptions options, long bytes)
        => bytes >= 0 &&
           bytes <= options.MaxBytesPerRun;

    private static bool ExtensionMatches(SafeFileGrantOptions options, SandboxAuditEvent auditEvent)
    {
        if (options.AllowedExtensions is null)
        {
            return true;
        }

        var resource = auditEvent.ResourceId!;
        var resourceExtension = Path.GetExtension(resource);
        if (auditEvent.Fields?.TryGetValue(PathExtensionField, out var evidence) == true)
        {
            if (!IsValidExtension(evidence))
            {
                return false;
            }

            return options.AllowedExtensions.Contains(evidence) &&
                (ResourceContainsRedaction(resource) ||
                 string.Equals(resourceExtension, evidence, StringComparison.OrdinalIgnoreCase));
        }

        return options.AllowedExtensions.Contains(resourceExtension);
    }

    private static bool WriteDispositionMatches(SafeFileGrantOptions options, SandboxAuditEvent auditEvent)
    {
        if (auditEvent.CapabilityId != FileWrite)
        {
            return true;
        }

        if (auditEvent.Fields?.TryGetValue(WriteDispositionField, out var disposition) != true)
        {
            return false;
        }

        return disposition switch
        {
            "create" => options.AllowCreate,
            "overwrite" => options.AllowOverwrite,
            _ => false
        };
    }

    private static bool IsValidExtension(string extension)
    {
        if (extension.Length <= 1 || extension[0] != '.')
        {
            return false;
        }

        for (var i = 1; i < extension.Length; i++)
        {
            var c = extension[i];
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '/' or '\\' or '.')
            {
                return false;
            }
        }

        return true;
    }

    private static bool ResourceContainsRedaction(string resource)
        => resource.Contains("/" + RedactedSegment, StringComparison.Ordinal);
}
