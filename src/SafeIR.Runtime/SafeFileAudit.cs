namespace SafeIR.Runtime;

using SafeIR;

internal static class SafeFileAudit
{
    public static void Read(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => WriteEvent(context, startedAt, success, "file.readText", "file.read", SandboxEffect.FileRead, resource, bytes, error);

    public static void Write(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => WriteEvent(context, startedAt, success, "file.writeText", "file.write", SandboxEffect.FileWrite, resource, bytes, error);

    private static void WriteEvent(
        SandboxContext context,
        DateTimeOffset startedAt,
        bool success,
        string bindingId,
        string capabilityId,
        SandboxEffect effect,
        string resource,
        long? bytes,
        SandboxErrorCode? error)
        => context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            success,
            BindingId: bindingId,
            CapabilityId: capabilityId,
            Effect: effect,
            ResourceId: Sanitize(resource),
            ErrorCode: error,
            Bytes: bytes));

    private static string Sanitize(string value) => value.Replace('\\', '/');
}
