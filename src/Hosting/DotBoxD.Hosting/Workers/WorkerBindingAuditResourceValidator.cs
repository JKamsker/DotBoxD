using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting;

internal static class WorkerBindingAuditResourceValidator
{
    public static bool Matches(
        ExecutionPlan plan,
        SandboxAuditEvent auditEvent,
        BindingSignature binding,
        DateTimeOffset grantClock)
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
            return true;
        }

        return plan.Policy.TryGetGrant(binding.RequiredCapability, grantClock, out var grant) &&
               descriptor.AuditResourceValidator(grant, auditEvent);
    }
}
