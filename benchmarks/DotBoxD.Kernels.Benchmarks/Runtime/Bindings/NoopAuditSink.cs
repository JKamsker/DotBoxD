using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime.Bindings;

internal sealed class NoopAuditSink : IAuditSink
{
    public static NoopAuditSink Instance { get; } = new();

    public long EventsWritten => 0;

    public void Write(SandboxAuditEvent auditEvent) { }

    public bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash)
        => false;
}
