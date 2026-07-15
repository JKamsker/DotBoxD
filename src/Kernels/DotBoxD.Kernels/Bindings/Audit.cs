using System.Collections.ObjectModel;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

public sealed record SandboxRunId(Guid Value)
{
    internal static SandboxRunId Suppressed { get; } = new(Guid.Empty);

    public static SandboxRunId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public enum AuditLevel
{
    None,
    Summary,
    PerCall,
    PerResource,
    FullInputOutput
}

public static class BindingAuditKinds
{
    public const string BindingCall = "BindingCall";
    public const string SandboxLog = "SandboxLog";
    public const string PluginMessage = "PluginMessage";
}

internal sealed class OwnedAuditEventSnapshot(IList<SandboxAuditEvent> list)
    : ReadOnlyCollection<SandboxAuditEvent>(list);

public interface IAuditSink
{
    long EventsWritten { get; }

    void Write(SandboxAuditEvent auditEvent);

    bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash);
}

public sealed class InMemoryAuditSink : IAuditSink
{
    private static readonly IReadOnlyList<SandboxAuditEvent> EmptySnapshot =
        new OwnedAuditEventSnapshot(Array.Empty<SandboxAuditEvent>());

    internal static IReadOnlyList<SandboxAuditEvent> EmptyEventSnapshot => EmptySnapshot;

    private List<SandboxAuditEvent>? _events;
    private long _sequence;

    public IReadOnlyList<SandboxAuditEvent> Events
    {
        get
        {
            return CopyEvents();
        }
    }

    public long EventsWritten
    {
        get
        {
            var events = Volatile.Read(ref _events);
            if (events is null)
            {
                return 0;
            }

            lock (events)
            {
                return _sequence;
            }
        }
    }

    /// <summary>
    /// Produces a single owned, immutable snapshot of the recorded events.
    /// The returned <see cref="ReadOnlyCollection{T}"/> wraps a fresh array that is not
    /// retained by the sink, so result construction can adopt it without copying again.
    /// </summary>
    internal IReadOnlyList<SandboxAuditEvent> SnapshotEvents()
    {
        var events = CopyEvents();
        return events.Length == 0
            ? EmptySnapshot
            : new OwnedAuditEventSnapshot(events);
    }

    private SandboxAuditEvent[] CopyEvents()
    {
        var events = Volatile.Read(ref _events);
        if (events is null)
        {
            return Array.Empty<SandboxAuditEvent>();
        }

        lock (events)
        {
            return events.Count == 0 ? Array.Empty<SandboxAuditEvent>() : events.ToArray();
        }
    }

    public void Write(SandboxAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var events = GetOrCreateEvents();
        lock (events)
        {
            AppendUnderLock(events, auditEvent);
        }
    }

    internal void WriteBindingInvocation(
        BindingAuditInvocationSink invocation,
        SandboxAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var events = GetOrCreateEvents();
        lock (events)
        {
            if (invocation.ShouldSuppressTerminalUnderLock(auditEvent))
            {
                return;
            }

            AppendUnderLock(events, auditEvent);
            invocation.RecordTerminalEvidenceUnderLock(auditEvent);
        }
    }

    internal bool TrySealBindingInvocationSuccess(BindingAuditInvocationSink invocation)
    {
        var events = GetOrCreateEvents();
        lock (events)
        {
            return invocation.TrySealSuccessUnderLock();
        }
    }

    internal void EnsureBindingInvocationFailure(
        BindingAuditInvocationSink invocation,
        SandboxErrorCode errorCode)
    {
        var events = GetOrCreateEvents();
        lock (events)
        {
            if (!invocation.HasFailureEvidenceUnderLock(errorCode))
            {
                // This factory reads only immutable context metadata and the clock;
                // it cannot re-enter the audit sink while its event gate is held.
                AppendUnderLock(events, invocation.CreateRequiredFailureAudit(errorCode));
                invocation.RecordFailureEvidenceUnderLock(errorCode);
            }

            invocation.SealUnderLock();
        }
    }

    public bool HasBindingAuditSince(
        BindingDescriptor descriptor,
        long checkpoint,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash)
    {
        var events = Volatile.Read(ref _events);
        if (events is null)
        {
            return false;
        }

        lock (events)
        {
            // Sequence numbers are assigned monotonically on append (Write sets
            // SequenceNumber = ++_sequence) and _events is never reordered or
            // pruned, so _events[i].SequenceNumber == i + 1. A checkpoint is the
            // sequence count recorded before the current binding call, which means
            // the first event with SequenceNumber > checkpoint lives at list index
            // checkpoint. Start enumeration there instead of rescanning prior
            // events, avoiding O(N^2) enforcement work over a run.
            for (var index = StartIndexAfter(checkpoint, events.Count); index < events.Count; index++)
            {
                var e = events[index];
                if (BindingAuditMatches(e, descriptor, success, expectedErrorCode, runId, moduleHash, policyHash))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static bool BindingAuditMatches(
        SandboxAuditEvent auditEvent,
        BindingDescriptor descriptor,
        bool success,
        SandboxErrorCode? expectedErrorCode,
        SandboxRunId runId,
        string moduleHash,
        string policyHash)
        => auditEvent.RunId == runId &&
           auditEvent.Success == success &&
           StringComparer.Ordinal.Equals(auditEvent.Kind, descriptor.AuditKind) &&
           StringComparer.Ordinal.Equals(auditEvent.BindingId, descriptor.Id) &&
           CapabilityMatches(auditEvent, descriptor) &&
           EffectMatches(auditEvent, descriptor) &&
           !string.IsNullOrWhiteSpace(auditEvent.ResourceId) &&
           HasRequiredFields(auditEvent, moduleHash, policyHash) &&
           ResultMatches(auditEvent, success, expectedErrorCode);

    private List<SandboxAuditEvent> GetOrCreateEvents()
    {
        var events = Volatile.Read(ref _events);
        if (events is not null)
        {
            return events;
        }

        var created = new List<SandboxAuditEvent>();
        return Interlocked.CompareExchange(ref _events, created, null) ?? created;
    }

    private void AppendUnderLock(List<SandboxAuditEvent> events, SandboxAuditEvent auditEvent)
    {
        var sequence = ++_sequence;
        events.Add(auditEvent with { SequenceNumber = sequence });
    }

    private static int StartIndexAfter(long checkpoint, int eventCount)
    {
        // Fail closed against an out-of-range checkpoint: a negative checkpoint
        // means "scan all events", and one beyond the recorded count yields an
        // empty range. The bounded clamp keeps the index a valid list offset.
        if (checkpoint <= 0)
        {
            return 0;
        }

        return checkpoint >= eventCount ? eventCount : (int)checkpoint;
    }

    private static bool CapabilityMatches(SandboxAuditEvent auditEvent, BindingDescriptor descriptor)
        => descriptor.RequiredCapability is null ||
           StringComparer.Ordinal.Equals(auditEvent.CapabilityId, descriptor.RequiredCapability);

    private static bool EffectMatches(SandboxAuditEvent auditEvent, BindingDescriptor descriptor)
    {
        if (auditEvent.Effect == SandboxEffect.None ||
            (auditEvent.Effect & ~descriptor.Effects) != SandboxEffect.None)
        {
            return false;
        }

        var nonCpuEffects = descriptor.Effects & ~SandboxEffect.Cpu;
        return nonCpuEffects == SandboxEffect.None ||
               (auditEvent.Effect & nonCpuEffects) != SandboxEffect.None;
    }

    private static bool ResultMatches(SandboxAuditEvent auditEvent, bool success, SandboxErrorCode? expectedErrorCode)
        => success
            ? auditEvent.ErrorCode is null
            : auditEvent.ErrorCode is not null && auditEvent.ErrorCode == expectedErrorCode;

    private static bool HasRequiredFields(SandboxAuditEvent auditEvent, string moduleHash, string policyHash)
    {
        if (auditEvent.Fields is null ||
            !auditEvent.Fields.TryGetValue("resourceKind", out var resourceKind) ||
            string.IsNullOrWhiteSpace(resourceKind) ||
            !auditEvent.Fields.TryGetValue("durationMs", out var durationMs) ||
            !auditEvent.Fields.TryGetValue("moduleHash", out var auditModuleHash) ||
            !StringComparer.Ordinal.Equals(auditModuleHash, moduleHash) ||
            !auditEvent.Fields.TryGetValue("policyHash", out var auditPolicyHash) ||
            !StringComparer.Ordinal.Equals(auditPolicyHash, policyHash))
        {
            return false;
        }

        return double.TryParse(
                durationMs,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) &&
            parsed >= 0;
    }
}
