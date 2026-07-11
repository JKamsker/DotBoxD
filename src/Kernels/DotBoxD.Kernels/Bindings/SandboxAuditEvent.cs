using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

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
    long? Bytes = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    long SequenceNumber = 0)
{
    private const string AuditByteCounterMessage = "audit byte counters must be non-negative.";

    private SandboxRunId _runId = RunId ?? throw new ArgumentNullException(nameof(RunId));
    private string _kind = RequireKind(Kind);
    private bool _success = Success;
    private SandboxEffect _effect = RequireKnownEffect(Effect, nameof(Effect));
    private SandboxErrorCode? _errorCode = RequireTerminalState(Success, ErrorCode, nameof(ErrorCode));
    private long? _bytes = RequireNonNegative(Bytes, nameof(Bytes));
    private IReadOnlyDictionary<string, string>? _fields = CopyFields(Fields);

    public SandboxRunId RunId { get => _runId; init => _runId = value ?? throw new ArgumentNullException(nameof(RunId)); }
    public string Kind { get => _kind; init => _kind = RequireKind(value); }
    public bool Success
    {
        get => _success;
        init
        {
            _success = value;
            ValidateTerminalState(nameof(Success));
        }
    }

    public SandboxEffect Effect { get => _effect; init => _effect = RequireKnownEffect(value, nameof(Effect)); }
    public SandboxErrorCode? ErrorCode
    {
        get => _errorCode;
        init
        {
            _errorCode = RequireKnownErrorCode(value, nameof(ErrorCode));
            ValidateTerminalState(nameof(ErrorCode));
        }
    }

    public long? Bytes { get => _bytes; init => _bytes = RequireNonNegative(value, nameof(Bytes)); }
    public IReadOnlyDictionary<string, string>? Fields { get => _fields; init => _fields = CopyFields(value); }

    private static string RequireKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind, nameof(Kind));

        return kind;
    }

    private static SandboxEffect RequireKnownEffect(SandboxEffect effect, string paramName)
        => effect.ContainsOnlyKnownBits()
            ? effect
            : throw new ArgumentException("Audit event effects must contain only known effect bits.", paramName);

    private static SandboxErrorCode? RequireKnownErrorCode(SandboxErrorCode? errorCode, string paramName)
        => errorCode is null || Enum.IsDefined(errorCode.Value)
            ? errorCode
            : throw new ArgumentException("Audit event error codes must be defined.", paramName);

    private static SandboxErrorCode? RequireTerminalState(
        bool success,
        SandboxErrorCode? errorCode,
        string paramName)
    {
        errorCode = RequireKnownErrorCode(errorCode, paramName);
        if (success == (errorCode is null))
        {
            return errorCode;
        }

        throw new ArgumentException("Audit event success state and error code are contradictory.", paramName);
    }

    private void ValidateTerminalState(string paramName)
    {
        if (_success && _errorCode is not null)
        {
            throw new ArgumentException("Successful audit events cannot carry an error code.", paramName);
        }

        if (!_success && _errorCode is null)
        {
            throw new ArgumentException("Failed audit events must carry an error code.", paramName);
        }
    }

    private static long? RequireNonNegative(long? value, string paramName)
        => SandboxCounterGuards.RequireNonNegative(value, paramName, AuditByteCounterMessage);

    private static IReadOnlyDictionary<string, string>? CopyFields(IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null)
        {
            return null;
        }

        foreach (var field in fields)
        {
            if (field.Value is null)
            {
                throw new ArgumentException("Audit event fields cannot contain null values.", nameof(Fields));
            }
        }

        return ModelCopy.StringDictionary(fields);
    }
}
