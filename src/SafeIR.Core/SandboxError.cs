namespace SafeIR;

public sealed record SandboxError(
    SandboxErrorCode Code,
    string SafeMessage,
    string? DiagnosticId = null);

public enum SandboxErrorCode
{
    ParseError,
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
