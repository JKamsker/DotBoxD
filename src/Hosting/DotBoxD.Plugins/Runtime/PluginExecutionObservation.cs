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
    private readonly bool _succeeded = Succeeded;
    private readonly SandboxErrorCode? _errorCode = Defined(ErrorCode, nameof(ErrorCode));
    private readonly SandboxErrorCode? _fallbackReason = Defined(FallbackReason, nameof(FallbackReason));
    private readonly string _cacheStatus = ValidCompiledTelemetry(
        CacheStatus ?? throw new ArgumentNullException(nameof(CacheStatus)),
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
        init => _entrypoint = value ?? throw new ArgumentNullException(nameof(Entrypoint));
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
        init => _errorCode = Defined(value, nameof(ErrorCode));
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
            value ?? throw new ArgumentNullException(nameof(CacheStatus)),
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

    private static ExecutionMode Defined(ExecutionMode value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static SandboxErrorCode? Defined(SandboxErrorCode? value, string parameterName)
        => value is null || Enum.IsDefined(value.Value) ? value : throw new ArgumentOutOfRangeException(parameterName);

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
            ThrowIfInterpretedSuccess(requestedMode, actualMode, succeeded, parameterName);
        }

        return value;
    }

    private ExecutionMode ValidRequestedMode(ExecutionMode value)
    {
        if (HasCompiledTelemetry())
        {
            ThrowIfInterpretedSuccess(value, ActualMode, Succeeded, nameof(RequestedMode));
        }

        return value;
    }

    private ExecutionMode ValidActualMode(ExecutionMode value)
    {
        if (HasCompiledTelemetry())
        {
            ThrowIfInterpretedSuccess(RequestedMode, value, Succeeded, nameof(ActualMode));
        }

        return value;
    }

    private bool ValidSucceeded(bool value)
    {
        if (HasCompiledTelemetry())
        {
            ThrowIfInterpretedSuccess(RequestedMode, ActualMode, value, nameof(Succeeded));
        }

        return value;
    }

    private bool HasCompiledTelemetry()
        => CacheStatus != "None"
            || RuntimeForm is not null
            || CacheKey is not null
            || ArtifactHash is not null
            || MaterializationStatus is not null;

    private static void ThrowIfInterpretedSuccess(
        ExecutionMode requestedMode,
        ExecutionMode actualMode,
        bool succeeded,
        string parameterName)
    {
        if (succeeded && requestedMode == ExecutionMode.Interpreted && actualMode == ExecutionMode.Interpreted)
        {
            throw new ArgumentException(
                "Successful interpreted execution observations cannot include compiled artifact telemetry.",
                parameterName);
        }
    }
}
