using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Sandbox;

internal abstract class BindingAuditInvocationFlowNode
{
    internal abstract BindingAuditInvocationSink Invocation { get; }
    internal abstract BindingAuditInvocationFlowNode? Previous { get; }
}

internal sealed class BindingAuditInvocationFlowLink(
    BindingAuditInvocationSink invocation,
    BindingAuditInvocationFlowNode? previous) : BindingAuditInvocationFlowNode
{
    internal override BindingAuditInvocationSink Invocation { get; } = invocation;
    internal override BindingAuditInvocationFlowNode? Previous { get; } = previous;
}

/// <summary>
/// Serializes terminal audit writes with the runtime's required-audit decision for one
/// binding invocation. The current invocation flows into the binding's async
/// continuation so a terminal write that loses to the runtime fallback can be rejected.
/// </summary>
internal sealed class BindingAuditInvocationSink : BindingAuditInvocationFlowNode, IAuditSink
{
    private static readonly AsyncLocal<BindingAuditInvocationFlowNode?> CurrentInvocation = new();

    private readonly SandboxContext _context;
    private readonly InMemoryAuditSink _destination;
    private readonly BindingDescriptor _descriptor;
    private readonly BindingAuditInvocationFlowNode? _previous;
    private ulong _failureEvidence;
    private bool _hasSuccessEvidence;
    private bool _sealed;

    internal BindingAuditInvocationSink(
        SandboxContext context,
        InMemoryAuditSink destination,
        BindingDescriptor descriptor)
    {
        _context = context;
        _destination = destination;
        _descriptor = descriptor;
        _previous = CurrentInvocation.Value;
        CurrentInvocation.Value = this;
    }

    internal override BindingAuditInvocationSink Invocation => this;
    internal override BindingAuditInvocationFlowNode? Previous => _previous;

    public long EventsWritten => _destination.EventsWritten;

    public void Write(SandboxAuditEvent auditEvent)
        => _destination.WriteBindingInvocation(this, auditEvent);

    public bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash)
        => _destination.HasBindingAuditSince(
            descriptor,
            checkpoint,
            success,
            expectedErrorCode,
            runId,
            moduleHash,
            policyHash);

    internal static BindingAuditInvocationSink? CurrentFor(SandboxContext context)
    {
        for (var current = CurrentInvocation.Value; current is not null; current = current.Previous)
        {
            if (ReferenceEquals(current.Invocation._context, context))
            {
                return current.Invocation;
            }
        }

        return null;
    }

    internal bool TrySealSuccess()
        => _destination.TrySealBindingInvocationSuccess(this);

    internal void EnsureFailure(SandboxErrorCode errorCode)
        => _destination.EnsureBindingInvocationFailure(this, errorCode);

    internal void Exit()
    {
        var current = CurrentInvocation.Value;
        if (current is null)
        {
            return;
        }

        if (ReferenceEquals(current.Invocation, this))
        {
            CurrentInvocation.Value = current.Previous;
            return;
        }

        var updated = RemoveFromFlow(current, this, out var removed);
        if (removed)
        {
            CurrentInvocation.Value = updated;
        }
    }

    internal bool ShouldSuppressTerminalUnderLock(SandboxAuditEvent auditEvent)
        => _sealed && MatchesInvocationTerminalIdentity(auditEvent);

    internal void RecordTerminalEvidenceUnderLock(SandboxAuditEvent auditEvent)
    {
        if (!IsRequiredTerminalEvidence(auditEvent))
        {
            return;
        }

        if (auditEvent.Success)
        {
            _hasSuccessEvidence = true;
        }
        else if (auditEvent.ErrorCode is { } errorCode)
        {
            _failureEvidence |= FailureCodeBit(errorCode);
        }
    }

    internal bool TrySealSuccessUnderLock()
    {
        _sealed = true;
        return _hasSuccessEvidence;
    }

    internal bool HasFailureEvidenceUnderLock(SandboxErrorCode errorCode)
        => (_failureEvidence & FailureCodeBit(errorCode)) != 0;

    internal void RecordFailureEvidenceUnderLock(SandboxErrorCode errorCode)
        => _failureEvidence |= FailureCodeBit(errorCode);

    internal SandboxAuditEvent CreateRequiredFailureAudit(SandboxErrorCode errorCode)
        => _context.CreateRequiredBindingFailureAudit(_descriptor, errorCode);

    internal void SealUnderLock() => _sealed = true;

    private bool MatchesInvocationTerminalIdentity(SandboxAuditEvent auditEvent)
        => auditEvent.RunId == _context.RunId &&
           StringComparer.Ordinal.Equals(auditEvent.Kind, _descriptor.AuditKind) &&
           StringComparer.Ordinal.Equals(auditEvent.BindingId, _descriptor.Id);

    private bool IsRequiredTerminalEvidence(SandboxAuditEvent auditEvent)
        => InMemoryAuditSink.BindingAuditMatches(
            auditEvent,
            _descriptor,
            auditEvent.Success,
            auditEvent.ErrorCode,
            _context.RunId,
            _context.ModuleHash,
            _context.PolicyHash);

    private static ulong FailureCodeBit(SandboxErrorCode errorCode)
    {
        // All current error codes fit in one allocation-free mask. Future codes
        // outside the mask fail closed because their evidence cannot be claimed.
        var code = (int)errorCode;
        return (uint)code < 64 ? 1UL << code : 0;
    }

    // Rebuild only this ExecutionContext's chain. Detached continuations keep their
    // original nodes and still route late terminal writes through the sealed wrapper.
    private static BindingAuditInvocationFlowNode? RemoveFromFlow(
        BindingAuditInvocationFlowNode current,
        BindingAuditInvocationSink removed,
        out bool found)
    {
        if (ReferenceEquals(current.Invocation, removed))
        {
            found = true;
            return current.Previous;
        }

        if (current.Previous is null)
        {
            found = false;
            return current;
        }

        var previous = RemoveFromFlow(current.Previous, removed, out found);
        return found
            ? new BindingAuditInvocationFlowLink(current.Invocation, previous)
            : current;
    }
}

internal readonly struct BindingAuditInvocation(
    long checkpoint,
    BindingAuditInvocationSink? sink = null) : IDisposable
{
    internal long Checkpoint { get; } = checkpoint;
    internal BindingAuditInvocationSink? Sink { get; } = sink;

    public void Dispose() => Sink?.Exit();
}
