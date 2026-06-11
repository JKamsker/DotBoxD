namespace SafeIR;

public sealed record SandboxRunId(Guid Value)
{
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

public sealed record SandboxAuditEvent(
    SandboxRunId RunId,
    string Kind,
    DateTimeOffset Timestamp,
    bool Success,
    string? BindingId = null,
    string? CapabilityId = null,
    SandboxEffect Effect = SandboxEffect.None,
    string? ResourceId = null,
    SandboxErrorCode? ErrorCode = null,
    string? Message = null,
    long? Bytes = null);

public interface IAuditSink
{
    void Write(SandboxAuditEvent auditEvent);
}

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly List<SandboxAuditEvent> _events = [];

    public IReadOnlyList<SandboxAuditEvent> Events => _events;

    public void Write(SandboxAuditEvent auditEvent) => _events.Add(auditEvent);
}
