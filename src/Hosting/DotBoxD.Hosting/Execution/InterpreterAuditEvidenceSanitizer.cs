using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Execution;

using DotBoxD.Kernels;

internal sealed class InterpreterAuditEvidenceSanitizer
{
    private const string DebugTraceKind = "DebugTrace";

    private readonly ExecutionPlan _plan;
    private readonly string _entrypoint;
    private readonly SandboxExecutionOptions _options;
    private readonly SandboxExecutionResult _result;
    private readonly SandboxAuditEvent _summary;
    private readonly SandboxRunId _runId;
    private readonly InterpreterDebugTraceValidator _debugTraceValidator;
    private readonly InMemoryAuditSink _validationAudit = new();
    private readonly InMemoryAuditSink _publicationAudit = new();
    private readonly Dictionary<string, int> _observedBindingCalls = new(StringComparer.Ordinal);
    private long _expectedSequenceNumber = 1;
    private long _observedBindingBaseFuel;
    private int _observedHostCalls;
    private int _observedLogEvents;
    private SandboxWorkerBindingEvidence.ObservedBindingBytes _observedBytes;
    private SandboxWorkerBindingEvidence.DeterministicRandomAuditSequence _deterministicRandom;
    private SandboxWorkerBindingEvidenceSequence _bindingEvidenceSequence;

    public InterpreterAuditEvidenceSanitizer(
        ExecutionPlan plan,
        string entrypoint,
        SandboxExecutionOptions options,
        SandboxExecutionResult result,
        SandboxAuditEvent summary)
    {
        _plan = plan;
        _entrypoint = entrypoint;
        _options = options;
        _result = result;
        _summary = summary;
        _runId = options.RunId ?? summary.RunId;
        _debugTraceValidator = new InterpreterDebugTraceValidator(plan, options, _runId);
        _deterministicRandom = SandboxWorkerBindingEvidence.DeterministicRandomAuditSequence.Create(plan);
        _bindingEvidenceSequence = SandboxWorkerBindingEvidenceSequence.Create(result);
    }

    public bool TrySanitize(
        out SandboxExecutionResult validationResult,
        out IReadOnlyList<SandboxAuditEvent> sanitizedAuditEvents)
    {
        validationResult = null!;
        sanitizedAuditEvents = [];
        foreach (var auditEvent in _result.AuditEvents)
        {
            if (!TryAcceptInSequence(auditEvent))
            {
                return false;
            }
        }

        var observedEvidence = new SandboxWorkerAuditEvidence(
            _summary,
            _observedHostCalls,
            _observedLogEvents,
            _observedBindingBaseFuel,
            _observedBytes);
        if (!SandboxWorkerAuditEvidenceCollector.UsageMatches(_result.ResourceUsage, observedEvidence))
        {
            return false;
        }

        sanitizedAuditEvents = _publicationAudit.OwnedEventSnapshot();
        validationResult = _result with { AuditEvents = _validationAudit.OwnedEventSnapshot() };
        return true;
    }

    private bool TryAcceptInSequence(SandboxAuditEvent auditEvent)
    {
        if (auditEvent.SequenceNumber != _expectedSequenceNumber++)
        {
            return false;
        }

        if (ReferenceEquals(auditEvent, _summary))
        {
            return TryAcceptSummary(auditEvent);
        }

        if (auditEvent.Kind == DebugTraceKind)
        {
            return TryAcceptDebugTrace(auditEvent);
        }

        return IsBindingAudit(auditEvent.Kind)
            ? TryAcceptBindingAudit(auditEvent)
            : !_result.Succeeded;
    }

    private bool TryAcceptSummary(SandboxAuditEvent auditEvent)
    {
        if (auditEvent.RunId != _runId)
        {
            return false;
        }

        _validationAudit.Write(auditEvent);
        _publicationAudit.Write(auditEvent);
        return true;
    }

    private bool TryAcceptDebugTrace(SandboxAuditEvent auditEvent)
    {
        if (!_debugTraceValidator.Matches(auditEvent))
        {
            return !_result.Succeeded;
        }

        _publicationAudit.Write(auditEvent);
        return true;
    }

    private bool TryAcceptBindingAudit(SandboxAuditEvent auditEvent)
    {
        if (auditEvent.RunId != _runId ||
            !BindingAuditMatchesResult(auditEvent) ||
            !WorkerAuditValidator.InterpreterBindingMatches(
                _plan,
                _entrypoint,
                auditEvent,
                _plan.Policy.GrantClock))
        {
            return !_result.Succeeded;
        }

        if (!TryRecordBindingUsage(auditEvent))
        {
            return false;
        }

        _publicationAudit.Write(auditEvent);
        return true;
    }

    private bool BindingAuditMatchesResult(SandboxAuditEvent auditEvent)
        => auditEvent.Success ||
           (!_result.Succeeded && auditEvent.ErrorCode == _result.Error!.Code);

    private bool TryRecordBindingUsage(SandboxAuditEvent auditEvent)
    {
        if (!SandboxWorkerBindingEvidence.TryRecordBindingEvidence(
                _plan,
                auditEvent,
                _bindingEvidenceSequence.Next(auditEvent),
                _observedBindingCalls,
                ref _observedBindingBaseFuel,
                ref _observedBytes,
                ref _deterministicRandom,
                _plan.Policy.GrantClock,
                out var representsCall))
        {
            return false;
        }

        if (!representsCall)
        {
            return true;
        }

        try
        {
            _observedHostCalls = checked(_observedHostCalls + 1);
            _observedLogEvents = checked(
                _observedLogEvents + (auditEvent.Kind == BindingAuditKinds.SandboxLog ? 1 : 0));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsBindingAudit(string kind)
        => kind is BindingAuditKinds.BindingCall or
           BindingAuditKinds.SandboxLog or
           BindingAuditKinds.PluginMessage;
}
