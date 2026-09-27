using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal struct SandboxWorkerBindingEvidenceSequence
{
    private readonly SandboxErrorCode? _terminalFailureCode;
    private readonly long _terminalBindingSequenceNumber;
    private SandboxAuditEvent? _previousBindingAudit;

    private SandboxWorkerBindingEvidenceSequence(
        SandboxErrorCode? terminalFailureCode,
        long terminalBindingSequenceNumber)
    {
        _terminalFailureCode = terminalFailureCode;
        _terminalBindingSequenceNumber = terminalBindingSequenceNumber;
        _previousBindingAudit = null;
    }

    public static SandboxWorkerBindingEvidenceSequence Create(SandboxExecutionResult result)
    {
        var terminalBindingSequenceNumber = 0L;
        foreach (var auditEvent in result.AuditEvents)
        {
            if (IsBindingAudit(auditEvent.Kind))
            {
                terminalBindingSequenceNumber = auditEvent.SequenceNumber;
            }
        }

        return new SandboxWorkerBindingEvidenceSequence(
            !result.Succeeded ? result.Error?.Code : null,
            terminalBindingSequenceNumber);
    }

    public SandboxWorkerBindingEvidenceRelationship Next(SandboxAuditEvent auditEvent)
    {
        var relationship = RelationshipToPrevious(auditEvent);
        _previousBindingAudit = auditEvent;
        return relationship;
    }

    private SandboxWorkerBindingEvidenceRelationship RelationshipToPrevious(
        SandboxAuditEvent auditEvent)
    {
        if (!MatchesTerminalFailure(auditEvent))
        {
            return SandboxWorkerBindingEvidenceRelationship.Ordinary;
        }

        // A binding can emit success before the runtime checks its return value,
        // deadline, or cancellation. The adjacent terminal failure then describes
        // that same charged call, while retaining both audit records.
        if (_previousBindingAudit is
            {
                Success: true,
                BindingId: { } previousBindingId
            } previous &&
            string.Equals(previousBindingId, auditEvent.BindingId, StringComparison.Ordinal) &&
            previous.SequenceNumber == auditEvent.SequenceNumber - 1)
        {
            return SandboxWorkerBindingEvidenceRelationship.TerminalFailureAfterSuccess;
        }

        return _terminalFailureCode == SandboxErrorCode.QuotaExceeded
            ? SandboxWorkerBindingEvidenceRelationship.TerminalQuotaFailure
            : SandboxWorkerBindingEvidenceRelationship.Ordinary;
    }

    private bool MatchesTerminalFailure(SandboxAuditEvent auditEvent)
        => _terminalFailureCode is SandboxErrorCode.QuotaExceeded or
                SandboxErrorCode.Timeout or SandboxErrorCode.Cancelled &&
           !auditEvent.Success &&
           auditEvent.ErrorCode == _terminalFailureCode &&
           auditEvent.SequenceNumber == _terminalBindingSequenceNumber;

    private static bool IsBindingAudit(string kind)
        => kind is BindingAuditKinds.BindingCall or
           BindingAuditKinds.SandboxLog or
           BindingAuditKinds.PluginMessage;
}

internal enum SandboxWorkerBindingEvidenceRelationship
{
    Ordinary,
    TerminalQuotaFailure,
    TerminalFailureAfterSuccess
}
