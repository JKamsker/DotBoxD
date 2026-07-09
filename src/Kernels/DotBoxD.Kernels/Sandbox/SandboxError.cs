namespace DotBoxD.Kernels.Sandbox;

public sealed record SandboxError(
    SandboxErrorCode Code,
    string SafeMessage,
    string? DiagnosticId = null)
{
    private SandboxErrorCode _code = ValidateCode(Code);
    private string _safeMessage = ValidateSafeMessage(SafeMessage);

    public SandboxErrorCode Code
    {
        get => _code;
        init
        {
            _code = ValidateCode(value);
        }
    }

    public string SafeMessage
    {
        get => _safeMessage;
        init
        {
            _safeMessage = ValidateSafeMessage(value);
        }
    }

    private static SandboxErrorCode ValidateCode(SandboxErrorCode value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(Code), value, "Unknown sandbox error code.");
        }

        return value;
    }

    private static string ValidateSafeMessage(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(SafeMessage));
        return value;
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
