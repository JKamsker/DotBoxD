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
    private readonly string _entrypoint = RequiredText(Entrypoint, nameof(Entrypoint));
    private readonly ExecutionMode _requestedMode = Defined(RequestedMode, nameof(RequestedMode));
    private readonly ExecutionMode _actualMode = Defined(ActualMode, nameof(ActualMode));
    private readonly bool _succeeded = ValidSucceeded(Succeeded, ErrorCode);
    private readonly SandboxErrorCode? _errorCode = ValidErrorCode(ErrorCode, Succeeded);
    private readonly SandboxErrorCode? _fallbackReason = Defined(FallbackReason, nameof(FallbackReason));
    private readonly string _cacheStatus = ValidCompiledTelemetry(
        RequiredText(CacheStatus, nameof(CacheStatus)),
        nameof(CacheStatus),
        RequestedMode,
        ActualMode,
        Succeeded,
        CacheStatus != "None");
    private readonly string? _runtimeForm = ValidCompiledTelemetry(
        RuntimeForm,
        nameof(RuntimeForm),
        RequestedMode,
        ActualMode,
        Succeeded,
        RuntimeForm is not null);
    private readonly string? _cacheKey = ValidCompiledTelemetry(
        CacheKey,
        nameof(CacheKey),
        RequestedMode,
        ActualMode,
        Succeeded,
        CacheKey is not null);
    private readonly string? _artifactHash = ValidCompiledTelemetry(
        ArtifactHash,
        nameof(ArtifactHash),
        RequestedMode,
        ActualMode,
        Succeeded,
        ArtifactHash is not null);
    private readonly string? _materializationStatus = ValidCompiledTelemetry(
        MaterializationStatus,
        nameof(MaterializationStatus),
        RequestedMode,
        ActualMode,
        Succeeded,
        MaterializationStatus is not null);

    public string Entrypoint
    {
        get => _entrypoint;
        init => _entrypoint = RequiredText(value, nameof(Entrypoint));
    }

    public ExecutionMode RequestedMode
    {
        get => _requestedMode;
        init => _requestedMode = ValidRequestedMode(Defined(value, nameof(RequestedMode)));
    }

    public ExecutionMode ActualMode
    {
        get => _actualMode;
        init => _actualMode = ValidActualMode(Defined(value, nameof(ActualMode)));
    }

    public bool Succeeded
    {
        get => _succeeded;
        init => _succeeded = ValidSucceeded(value);
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
        init => _cacheStatus = ValidCompiledTelemetry(
            RequiredText(value, nameof(CacheStatus)),
            nameof(CacheStatus),
            RequestedMode,
            ActualMode,
            Succeeded,
            value != "None");
    }

    public string? RuntimeForm
    {
        get => _runtimeForm;
        init => _runtimeForm = ValidCompiledTelemetry(
            value,
            nameof(RuntimeForm),
            RequestedMode,
            ActualMode,
            Succeeded,
            value is not null);
    }

    public string? CacheKey
    {
        get => _cacheKey;
        init => _cacheKey = ValidCompiledTelemetry(
            value,
            nameof(CacheKey),
            RequestedMode,
            ActualMode,
            Succeeded,
            value is not null);
    }

    public string? ArtifactHash
    {
        get => _artifactHash;
        init => _artifactHash = ValidCompiledTelemetry(
            value,
            nameof(ArtifactHash),
            RequestedMode,
            ActualMode,
            Succeeded,
            value is not null);
    }

    public string? MaterializationStatus
    {
        get => _materializationStatus;
        init => _materializationStatus = ValidCompiledTelemetry(
            value,
            nameof(MaterializationStatus),
            RequestedMode,
            ActualMode,
            Succeeded,
            value is not null);
    }

    private static string RequiredText(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty or whitespace.", parameterName)
            : value;
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

    private static T ValidCompiledTelemetry<T>(
        T value,
        string parameterName,
        ExecutionMode requestedMode,
        ExecutionMode actualMode,
        bool succeeded,
        bool hasCompiledTelemetry)
    {
        if (hasCompiledTelemetry)
        {
            ThrowIfInterpretedSuccess(
                requestedMode,
                actualMode,
                succeeded,
                hasCompiledTelemetry,
                parameterName);
        }

        return value;
    }

    private ExecutionMode ValidRequestedMode(ExecutionMode value)
    {
        if (HasCompiledTelemetry())
        {
            ThrowIfInterpretedSuccess(value, ActualMode, Succeeded, hasCompiledTelemetry: true, nameof(RequestedMode));
        }

        return value;
    }

    private ExecutionMode ValidActualMode(ExecutionMode value)
    {
        if (HasCompiledTelemetry())
        {
            ThrowIfInterpretedSuccess(RequestedMode, value, Succeeded, hasCompiledTelemetry: true, nameof(ActualMode));
        }

        return value;
    }

    private bool ValidSucceeded(bool value)
    {
        ValidateTerminalState(value, ErrorCode, nameof(Succeeded));
        ThrowIfInterpretedSuccess(
            RequestedMode,
            ActualMode,
            value,
            HasCompiledTelemetry(),
            nameof(Succeeded));
        return value;
    }

    private bool HasCompiledTelemetry()
        => HasCompiledTelemetry(CacheStatus, RuntimeForm, CacheKey, ArtifactHash, MaterializationStatus);

    private static bool HasCompiledTelemetry(
        string? cacheStatus,
        string? runtimeForm,
        string? cacheKey,
        string? artifactHash,
        string? materializationStatus)
        => cacheStatus is not null && cacheStatus != "None"
            || runtimeForm is not null
            || cacheKey is not null
            || artifactHash is not null
            || materializationStatus is not null;

    private static void ThrowIfInterpretedSuccess(
        ExecutionMode requestedMode,
        ExecutionMode actualMode,
        bool succeeded,
        bool hasCompiledTelemetry,
        string parameterName)
    {
        if (hasCompiledTelemetry &&
            succeeded &&
            requestedMode == ExecutionMode.Interpreted &&
            actualMode == ExecutionMode.Interpreted)
        {
            throw new ArgumentException(
                "Successful interpreted execution observations cannot include compiled artifact telemetry.",
                parameterName);
        }
    }
}
