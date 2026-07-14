using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting;

internal static class WorkerBindingAuditResourceValidator
{
    public static bool Matches(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        BindingSignature binding,
        DateTimeOffset grantClock,
        bool allowInProcessEvidence = false)
    {
        if (!auditEvent.Success ||
            auditEvent.Kind != BindingAuditKinds.BindingCall ||
            binding.RequiredCapability is null)
        {
            return true;
        }

        var descriptor = plan.Bindings.GetDescriptor(auditEvent.BindingId!);
        if (descriptor.AuditResourceValidator is null)
        {
            // Host-service descriptors execute in-process and construct this evidence locally.
            // A worker must provide a grant-aware validator because its evidence crossed a process boundary.
            return allowInProcessEvidence || !IsHostServiceAudit(auditEvent);
        }

        return plan.Policy.TryGetGrant(binding.RequiredCapability, grantClock, out var grant) &&
               descriptor.AuditResourceValidator(grant, auditEvent);
    }

    private static bool IsHostServiceAudit(SandboxAuditEvent auditEvent)
        => auditEvent.Fields is not null &&
           auditEvent.Fields.TryGetValue("resourceKind", out var resourceKind) &&
           string.Equals(resourceKind, "host-service", StringComparison.Ordinal);
}
