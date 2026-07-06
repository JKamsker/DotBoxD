namespace DotBoxD.Kernels.Sandbox;

public sealed record SandboxError
{
    private SandboxErrorCode _code;
    private string _safeMessage = string.Empty;

    public SandboxError(SandboxErrorCode Code, string SafeMessage, string? DiagnosticId = null)
    {
        this.Code = Code;
        this.SafeMessage = SafeMessage;
        this.DiagnosticId = DiagnosticId;
    }

    public SandboxErrorCode Code
    {
        get => _code;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(Code), value, "Unknown sandbox error code.");
            }

            _code = value;
        }
    }

    public string SafeMessage
    {
        get => _safeMessage;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value, nameof(SafeMessage));
            _safeMessage = value;
        }
    }

    public string? DiagnosticId { get; init; }

    public void Deconstruct(out SandboxErrorCode Code, out string SafeMessage, out string? DiagnosticId)
    {
        Code = this.Code;
        SafeMessage = this.SafeMessage;
        DiagnosticId = this.DiagnosticId;
    }
}

public enum SandboxErrorCode
{
    ValidationError,
    PolicyDenied,
    PermissionDenied,
    NotFound,
    InvalidInput,
    QuotaExceeded,
    Timeout,
    Cancelled,
    BindingFailure,
    VerifierFailure,
    CacheInvalid,
    HostFailure
}
