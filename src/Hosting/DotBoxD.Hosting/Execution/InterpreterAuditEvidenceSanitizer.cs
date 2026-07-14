using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

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
    private readonly InMemoryAuditSink _validationAudit = new();
    private readonly InMemoryAuditSink _publicationAudit = new();
    private readonly Dictionary<string, int> _observedBindingCalls = new(StringComparer.Ordinal);
    private long _expectedSequenceNumber = 1;
    private long _observedBindingBaseFuel;
    private int _observedHostCalls;
    private int _observedLogEvents;
    private SandboxWorkerBindingEvidence.ObservedBindingBytes _observedBytes;
    private SandboxWorkerBindingEvidence.DeterministicRandomAuditSequence _deterministicRandom;

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
        _deterministicRandom = SandboxWorkerBindingEvidence.DeterministicRandomAuditSequence.Create(plan);
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
        if (!DebugTraceMatches(auditEvent))
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

    private bool DebugTraceMatches(SandboxAuditEvent auditEvent)
    {
        if (!DebugTraceEnvelopeMatches(auditEvent, out var category, out var nodeKind))
        {
            return false;
        }

        return category == "binding"
            ? BindingDebugTraceMatches(auditEvent, nodeKind)
            : NodeDebugTraceMatches(auditEvent, category);
    }

    private bool DebugTraceEnvelopeMatches(
        SandboxAuditEvent auditEvent,
        out string category,
        out string nodeKind)
    {
        category = string.Empty;
        nodeKind = string.Empty;
        return _options.EnableDebugTrace &&
            auditEvent.RunId == _runId &&
            auditEvent.Success &&
            auditEvent.ResourceId is null &&
            WorkerAuditValidator.CommonEnvelopeMatches(_plan, auditEvent) &&
            DebugFieldsMatch(auditEvent, out category, out nodeKind);
    }

    private bool BindingDebugTraceMatches(SandboxAuditEvent auditEvent, string nodeKind)
        => auditEvent.BindingId == nodeKind &&
           _plan.Bindings.TryGet(nodeKind, out var binding) &&
           auditEvent.CapabilityId == binding.RequiredCapability &&
           auditEvent.Effect == binding.Effects;

    private static bool NodeDebugTraceMatches(SandboxAuditEvent auditEvent, string category)
        => category is "statement" or "expression" &&
           auditEvent.BindingId is null &&
           auditEvent.CapabilityId is null &&
           auditEvent.Effect == SandboxEffect.None;

    private bool DebugFieldsMatch(
        SandboxAuditEvent auditEvent,
        out string category,
        out string nodeKind)
    {
        category = string.Empty;
        nodeKind = string.Empty;
        return auditEvent.Fields is { Count: 7 } fields &&
            DebugIdentityFieldsMatch(fields, out category, out nodeKind) &&
            DebugSourceFieldsMatch(fields);
    }

    private bool DebugIdentityFieldsMatch(
        IReadOnlyDictionary<string, string> fields,
        out string category,
        out string nodeKind)
    {
        category = string.Empty;
        nodeKind = string.Empty;
        return FieldEquals(fields, "moduleHash", _plan.ModuleHash) &&
            RequiredSafeField(fields, "functionId", out var functionId) &&
            _plan.FunctionLookup.ContainsKey(functionId) &&
            RequiredSafeField(fields, "category", out category) &&
            RequiredSafeField(fields, "nodeKind", out nodeKind);
    }

    private static bool DebugSourceFieldsMatch(IReadOnlyDictionary<string, string> fields)
        => RequiredInteger(fields, "sourceLine", out var sourceLine) &&
           sourceLine >= 0 &&
           RequiredInteger(fields, "sourceColumn", out var sourceColumn) &&
           sourceColumn >= 0 &&
           RequiredLong(fields, "fuelRemaining");

    private bool BindingAuditMatchesResult(SandboxAuditEvent auditEvent)
        => auditEvent.Success ||
           (!_result.Succeeded && auditEvent.ErrorCode == _result.Error!.Code);

    private bool TryRecordBindingUsage(SandboxAuditEvent auditEvent)
    {
        if (!SandboxWorkerBindingEvidence.TryRecordBindingEvidence(
                _plan,
                auditEvent,
                _observedBindingCalls,
                ref _observedBindingBaseFuel,
                ref _observedBytes,
                ref _deterministicRandom,
                _plan.Policy.GrantClock))
        {
            return false;
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

    private static bool RequiredSafeField(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out string value)
    {
        if (fields.TryGetValue(name, out var candidate) &&
            !string.IsNullOrWhiteSpace(candidate) &&
            WorkerAuditTextSafety.TextIsSafe(candidate))
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool RequiredInteger(
        IReadOnlyDictionary<string, string> fields,
        string name,
        out int value)
    {
        value = 0;
        return fields.TryGetValue(name, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool RequiredLong(
        IReadOnlyDictionary<string, string> fields,
        string name)
        => fields.TryGetValue(name, out var raw) &&
           long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static bool FieldEquals(
        IReadOnlyDictionary<string, string> fields,
        string name,
        string expected)
        => fields.TryGetValue(name, out var value) &&
           string.Equals(value, expected, StringComparison.Ordinal);

    private static bool IsBindingAudit(string kind)
        => kind is BindingAuditKinds.BindingCall or
           BindingAuditKinds.SandboxLog or
           BindingAuditKinds.PluginMessage;
}
