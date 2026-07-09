using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime;

public sealed record PluginExecutionObservation(
    string Entrypoint,
    ExecutionMode RequestedMode,
    ExecutionMode ActualMode,
    bool Succeeded,
    SandboxErrorCode? ErrorCode,
    SandboxErrorCode? FallbackReason,
    string CacheStatus,
    string? RuntimeForm,
    string? CacheKey,
    string? ArtifactHash,
    string? MaterializationStatus)
{
    private readonly string _entrypoint = Entrypoint ?? throw new ArgumentNullException(nameof(Entrypoint));
    private readonly ExecutionMode _requestedMode = Defined(RequestedMode, nameof(RequestedMode));
    private readonly ExecutionMode _actualMode = Defined(ActualMode, nameof(ActualMode));
    private readonly bool _succeeded = ValidSucceeded(Succeeded, ErrorCode);
    private readonly SandboxErrorCode? _errorCode = ValidErrorCode(ErrorCode, Succeeded);
    private readonly SandboxErrorCode? _fallbackReason = Defined(FallbackReason, nameof(FallbackReason));
    private readonly string _cacheStatus = CacheStatus ?? throw new ArgumentNullException(nameof(CacheStatus));

    public string Entrypoint
    {
        get => _entrypoint;
        init => _entrypoint = value ?? throw new ArgumentNullException(nameof(Entrypoint));
    }

    public ExecutionMode RequestedMode
    {
        get => _requestedMode;
        init => _requestedMode = Defined(value, nameof(RequestedMode));
    }

    public ExecutionMode ActualMode
    {
        get => _actualMode;
        init => _actualMode = Defined(value, nameof(ActualMode));
    }

    public bool Succeeded
    {
        get => _succeeded;
        init => _succeeded = ValidSucceeded(value, ErrorCode);
    }

    public SandboxErrorCode? ErrorCode
    {
        get => _errorCode;
        init => _errorCode = ValidErrorCode(value, Succeeded);
    }

    public SandboxErrorCode? FallbackReason
    {
        get => _fallbackReason;
        init => _fallbackReason = Defined(value, nameof(FallbackReason));
    }

    public string CacheStatus
    {
        get => _cacheStatus;
        init => _cacheStatus = value ?? throw new ArgumentNullException(nameof(CacheStatus));
    }

    private static ExecutionMode Defined(ExecutionMode value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static SandboxErrorCode? Defined(SandboxErrorCode? value, string parameterName)
        => value is null || Enum.IsDefined(value.Value) ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static bool ValidSucceeded(bool succeeded, SandboxErrorCode? errorCode)
    {
        ValidateTerminalState(succeeded, errorCode, nameof(Succeeded));
        return succeeded;
    }

    private static SandboxErrorCode? ValidErrorCode(SandboxErrorCode? errorCode, bool succeeded)
    {
        var defined = Defined(errorCode, nameof(ErrorCode));
        ValidateTerminalState(succeeded, defined, nameof(ErrorCode));
        return defined;
    }

    private static void ValidateTerminalState(bool succeeded, SandboxErrorCode? errorCode, string parameterName)
    {
        if (succeeded && errorCode is not null)
        {
            throw new ArgumentException("Successful plugin execution observations cannot carry an error code.", parameterName);
        }

        if (!succeeded && errorCode is null)
        {
            throw new ArgumentException("Failed plugin execution observations must carry an error code.", parameterName);
        }
    }
}
