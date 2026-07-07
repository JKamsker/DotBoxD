using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels;

public sealed record SandboxExecutionResult
{
    private IReadOnlyList<SandboxAuditEvent> _auditEvents = [];
    private SandboxResourceUsage _resourceUsage = null!;
    private string _moduleHash = null!;
    private string _planHash = null!;
    private string _policyHash = null!;
    private ExecutionMode _actualMode;
    private bool _succeeded;
    private SandboxValue? _value;
    private SandboxError? _error;

    public bool Succeeded
    {
        get => _succeeded;
        init
        {
            if (value && _error is not null)
            {
                throw new ArgumentException("Successful execution results cannot carry an error.", nameof(Error));
            }

            if (!value && _value is not null)
            {
                throw new ArgumentException("Failed execution results cannot carry a value.", nameof(Value));
            }

            _succeeded = value;
        }
    }

    public SandboxValue? Value
    {
        get => _value;
        init
        {
            if (!_succeeded && value is not null)
            {
                throw new ArgumentException("Failed execution results cannot carry a value.", nameof(Value));
            }

            _value = value;
        }
    }

    public SandboxError? Error
    {
        get => _error;
        init
        {
            if (_succeeded && value is not null)
            {
                throw new ArgumentException("Successful execution results cannot carry an error.", nameof(Error));
            }

            if (!_succeeded && value is null)
            {
                throw new ArgumentException("Failed execution results must carry an error.", nameof(Error));
            }

            _error = value;
        }
    }
    public required SandboxResourceUsage ResourceUsage
    {
        get => _resourceUsage;
        init => _resourceUsage = value ?? throw new ArgumentNullException(nameof(ResourceUsage));
    }

    public required IReadOnlyList<SandboxAuditEvent> AuditEvents
    {
        get => _auditEvents;
        init => _auditEvents = AdoptOrCopy(value);
    }

    // An already-owned, immutable snapshot (for example the one produced on the execution
    // hot path) can be adopted directly; any other input is still defensively copied so
    // external list/array identity never escapes into the public result.
    private static IReadOnlyList<SandboxAuditEvent> AdoptOrCopy(IReadOnlyList<SandboxAuditEvent> value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(AuditEvents));
        if (value is OwnedAuditEventSnapshot owned)
        {
            return owned;
        }

        ValidateExternalAuditEvents(value);
        return ModelCopy.List(value);
    }

    private static void ValidateExternalAuditEvents(IReadOnlyList<SandboxAuditEvent> value)
    {
        foreach (var auditEvent in value)
        {
            if (auditEvent is null)
            {
                throw new ArgumentException("Audit events cannot contain null entries.", nameof(AuditEvents));
            }
        }
    }

    public ExecutionMode ActualMode { get => _actualMode; init => _actualMode = RequireDefinedMode(value, nameof(ActualMode)); }
    public bool ExecutionDispatched { get; init; }
    public required string ModuleHash
    {
        get => _moduleHash;
        init => _moduleHash = value ?? throw new ArgumentNullException(nameof(ModuleHash));
    }

    public required string PlanHash
    {
        get => _planHash;
        init => _planHash = value ?? throw new ArgumentNullException(nameof(PlanHash));
    }

    public required string PolicyHash
    {
        get => _policyHash;
        init => _policyHash = value ?? throw new ArgumentNullException(nameof(PolicyHash));
    }

    public string? ArtifactHash { get; init; }

    private static ExecutionMode RequireDefinedMode(ExecutionMode mode, string paramName)
        => Enum.IsDefined(mode)
            ? mode
            : throw new ArgumentException("Execution result mode must be defined.", paramName);
}
