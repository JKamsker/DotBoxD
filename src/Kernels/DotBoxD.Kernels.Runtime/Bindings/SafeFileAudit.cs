using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

internal static class SafeFileAudit
{
    private const string PathExtensionField = "pathExtension";
    private const string WriteDispositionField = "writeDisposition";

    public static void Read(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => WriteEvent(
            context,
            startedAt,
            success,
            "file.readText",
            "file.read",
            SandboxEffect.FileRead,
            resource,
            bytes,
            false,
            error);

    public static void Write(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        bool targetExisted,
        SandboxErrorCode? error)
        => WriteEvent(
            context,
            startedAt,
            success,
            "file.writeText",
            "file.write",
            SandboxEffect.FileWrite,
            resource,
            bytes,
            targetExisted,
            error);

    private static void WriteEvent(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string bindingId,
        string capabilityId,
        SandboxEffect effect,
        string resource,
        long? bytes,
        bool targetExisted,
        SandboxErrorCode? error)
    {
        var fields = BindingAuditFields.CreateMutable(
            "file",
            startedAt,
            context.ModuleHash,
            context.PolicyHash,
            context.Policy.Deterministic,
            extraCapacity: success ? 2 : 0,
            bytesRead: bindingId == "file.readText" ? bytes : null,
            bytesWritten: bindingId == "file.writeText" ? bytes : null);
        if (success)
        {
            AddPathExtension(fields, resource);
            AddWriteDisposition(fields, bindingId, targetExisted);
        }

        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            success,
            BindingId: bindingId,
            CapabilityId: capabilityId,
            Effect: effect,
            ResourceId: Sanitize(resource),
            ErrorCode: error,
            Bytes: bytes,
            Fields: fields));
    }

    private static string Sanitize(string value)
        => AuditTextSanitizer.RedactPathSegments(value.Replace('\\', '/'));

    private static void AddPathExtension(IDictionary<string, string> fields, string resource)
    {
        var extension = Path.GetExtension(resource);
        if (!string.IsNullOrEmpty(extension))
        {
            fields[PathExtensionField] = extension;
        }
    }

    private static void AddWriteDisposition(IDictionary<string, string> fields, string bindingId, bool targetExisted)
    {
        if (bindingId == "file.writeText")
        {
            fields[WriteDispositionField] = targetExisted ? "overwrite" : "create";
        }
    }
}
