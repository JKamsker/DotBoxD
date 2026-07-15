using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting;

using DotBoxD.Kernels;

internal struct SandboxWorkerBindingEvidenceSequence
{
    private readonly bool _resultIsQuotaFailure;
    private readonly long _terminalBindingSequenceNumber;
    private SandboxAuditEvent? _previousBindingAudit;

    private SandboxWorkerBindingEvidenceSequence(
        bool resultIsQuotaFailure,
        long terminalBindingSequenceNumber)
    {
        _resultIsQuotaFailure = resultIsQuotaFailure;
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
            !result.Succeeded && result.Error?.Code == SandboxErrorCode.QuotaExceeded,
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
        if (!_resultIsQuotaFailure ||
            auditEvent.Success ||
            auditEvent.ErrorCode != SandboxErrorCode.QuotaExceeded ||
            auditEvent.SequenceNumber != _terminalBindingSequenceNumber)
        {
            return SandboxWorkerBindingEvidenceRelationship.Ordinary;
        }

        return _previousBindingAudit is
        {
            Success: true,
            BindingId: { } previousBindingId
        } previous &&
            string.Equals(previousBindingId, auditEvent.BindingId, StringComparison.Ordinal) &&
            previous.SequenceNumber == auditEvent.SequenceNumber - 1
                ? SandboxWorkerBindingEvidenceRelationship.TerminalQuotaFailureAfterSuccess
                : SandboxWorkerBindingEvidenceRelationship.TerminalQuotaFailure;
    }

    private static bool IsBindingAudit(string kind)
        => kind is BindingAuditKinds.BindingCall or
           BindingAuditKinds.SandboxLog or
           BindingAuditKinds.PluginMessage;
}

internal enum SandboxWorkerBindingEvidenceRelationship
{
    Ordinary,
    TerminalQuotaFailure,
    TerminalQuotaFailureAfterSuccess
}
