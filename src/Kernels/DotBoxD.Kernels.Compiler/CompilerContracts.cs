using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Compiler;

using DotBoxD.Kernels;

public delegate SandboxValue SandboxCompiledEntrypoint(SandboxContext context, SandboxValue input);

public sealed record CompileOptions(string Entrypoint, bool Optimize = false)
{
    private string _entrypoint = RequireEntrypoint(Entrypoint);

    public string Entrypoint
    {
        get => _entrypoint;
        init => _entrypoint = RequireEntrypoint(value);
    }

    private static string RequireEntrypoint(string? entrypoint)
    {
        ArgumentNullException.ThrowIfNull(entrypoint, nameof(Entrypoint));

        if (string.IsNullOrWhiteSpace(entrypoint))
        {
            throw new ArgumentException("Entrypoint must not be blank.", nameof(Entrypoint));
        }

        return entrypoint;
    }
}

public enum CompiledRuntimeFormKind
{
    LoadedAssembly,
    DynamicMethod
}

public enum CompiledCacheStatus
{
    None,
    Hit,
    Miss,
    Invalid,
    Recompiled
}

/// <summary>Describes the result of reading a compiled artifact from the persistent cache.</summary>
/// <remarks>
/// A <see cref="CompiledCacheStatus.Hit"/> result must include an artifact and cannot include an invalid reason.
/// <see cref="CompiledCacheStatus.Miss"/> and <see cref="CompiledCacheStatus.Invalid"/> results cannot include
/// an artifact. Only an invalid result can include an invalid reason, and invalid results must include one.
/// </remarks>
public sealed record CompiledCacheLookup(
    CompiledCacheStatus Status,
    CompiledArtifact? Artifact,
    string? InvalidReason = null)
{
    private CompiledCacheStatus _status = ValidateStatus(Status);
    private CompiledArtifact? _artifact = ValidateArtifact(Status, Artifact, InvalidReason);
    private string? _invalidReason = ValidateInvalidReason(Status, Artifact, InvalidReason);

    /// <summary>Gets the cache lookup state. Only hit, miss, and invalid are valid lookup states.</summary>
    public CompiledCacheStatus Status
    {
        get => _status;
        init
        {
            Validate(value, _artifact, _invalidReason);
            _status = value;
        }
    }

    /// <summary>Gets the cached artifact. Cache hits require an artifact; misses and invalid entries cannot include one.</summary>
    public CompiledArtifact? Artifact
    {
        get => _artifact;
        init
        {
            Validate(_status, value, _invalidReason);
            _artifact = value;
        }
    }

    /// <summary>Gets the invalid-cache diagnostic. Only invalid entries can include this value, and they must include it.</summary>
    public string? InvalidReason
    {
        get => _invalidReason;
        init
        {
            Validate(_status, _artifact, value);
            _invalidReason = value;
        }
    }

    private static void Validate(CompiledCacheStatus status, CompiledArtifact? artifact, string? invalidReason)
    {
        _ = ValidateStatus(status);
        _ = ValidateArtifact(status, artifact, invalidReason);
        _ = ValidateInvalidReason(status, artifact, invalidReason);
    }

    private static CompiledCacheStatus ValidateStatus(CompiledCacheStatus status)
        => status is CompiledCacheStatus.Hit or CompiledCacheStatus.Miss or CompiledCacheStatus.Invalid
            ? status
            : throw new ArgumentOutOfRangeException(nameof(Status), status, "Compiled cache lookup status is not supported.");

    private static CompiledArtifact? ValidateArtifact(
        CompiledCacheStatus status,
        CompiledArtifact? artifact,
        string? invalidReason)
        => (status, artifact, invalidReason) switch
        {
            (CompiledCacheStatus.Hit, null, _) => throw new ArgumentException(
                "Cache hits must include the cached artifact.",
                nameof(Artifact)),
            (CompiledCacheStatus.Miss or CompiledCacheStatus.Invalid, not null, _) => throw new ArgumentException(
                "Cache misses and invalid entries cannot include an artifact.",
                nameof(Artifact)),
            _ => artifact
        };

    private static string? ValidateInvalidReason(
        CompiledCacheStatus status,
        CompiledArtifact? artifact,
        string? invalidReason)
        => (status, artifact, invalidReason) switch
        {
            (CompiledCacheStatus.Invalid, _, var reason) when string.IsNullOrWhiteSpace(reason) => throw new ArgumentException(
                "Invalid cache entries must include the invalid reason.",
                nameof(InvalidReason)),
            (CompiledCacheStatus.Hit or CompiledCacheStatus.Miss, _, not null) => throw new ArgumentException(
                "Only invalid cache entries can include an invalid reason.",
                nameof(InvalidReason)),
            _ => invalidReason
        };
}

public sealed record CompiledArtifact
{
    private byte[] _assemblyBytes = [];
    private string _assemblyHash = string.Empty;
    private ArtifactManifest _manifest = null!;
    private VerificationResult _verification = null!;
    private SandboxCompiledEntrypoint _entrypoint = null!;
    private CompiledRuntimeFormKind _runtimeForm;
    private CompiledCacheStatus _cacheStatus;

    public CompiledArtifact(
        byte[] assemblyBytes,
        string assemblyHash,
        ArtifactManifest manifest,
        VerificationResult verification,
        SandboxCompiledEntrypoint entrypoint,
        CompiledRuntimeFormKind runtimeForm,
        CompiledCacheStatus cacheStatus = CompiledCacheStatus.None,
        string? cacheInvalidReason = null)
        : this(
            assemblyBytes,
            assemblyHash,
            manifest,
            verification,
            entrypoint,
            runtimeForm,
            cacheStatus,
            cacheInvalidReason,
            copyAssemblyBytes: true)
    {
    }

    internal CompiledArtifact(
        byte[] assemblyBytes,
        string assemblyHash,
        ArtifactManifest manifest,
        VerificationResult verification,
        SandboxCompiledEntrypoint entrypoint,
        CompiledRuntimeFormKind runtimeForm,
        CompiledCacheStatus cacheStatus,
        string? cacheInvalidReason,
        bool copyAssemblyBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(assemblyHash);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(entrypoint);

        RequireDefined(runtimeForm, nameof(runtimeForm), "Compiled runtime form is not supported.");
        RequireDefined(cacheStatus, nameof(cacheStatus), "Compiled cache status is not supported.");

        if (!verification.Succeeded)
        {
            throw new ArgumentException("Compiled runtime form must be verified or gated before execution.", nameof(verification));
        }

        if (!StringComparer.Ordinal.Equals(assemblyHash, verification.AssemblyHash) ||
            !StringComparer.Ordinal.Equals(assemblyHash, manifest.AssemblyHash))
        {
            throw new ArgumentException("Compiled artifact hash must match its manifest and verification result.", nameof(assemblyHash));
        }

        if (runtimeForm == CompiledRuntimeFormKind.DynamicMethod && assemblyBytes.Length != 0)
        {
            throw new ArgumentException(
                "DynamicMethod artifacts expose only the created delegate, not assembly bytes.",
                nameof(AssemblyBytes));
        }

        if (runtimeForm == CompiledRuntimeFormKind.LoadedAssembly && assemblyBytes.Length == 0)
        {
            throw new ArgumentException(
                "LoadedAssembly artifacts must include the verified assembly image used to create the delegate.",
                nameof(AssemblyBytes));
        }

        _assemblyBytes = copyAssemblyBytes ? assemblyBytes.ToArray() : assemblyBytes;
        _assemblyHash = assemblyHash;
        _manifest = manifest;
        _verification = verification;
        _entrypoint = entrypoint;
        _runtimeForm = runtimeForm;
        _cacheStatus = cacheStatus;
        CacheInvalidReason = cacheInvalidReason;
    }

    public byte[] AssemblyBytes
    {
        get => _assemblyBytes.ToArray();
        init => _assemblyBytes = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
    internal ReadOnlyMemory<byte> AssemblyBytesMemory => _assemblyBytes;
    internal byte[] AssemblyBytesUnsafe => _assemblyBytes;
    public string AssemblyHash
    {
        get => _assemblyHash;
        init => _assemblyHash = RequireNonNull(value, nameof(AssemblyHash));
    }
    public ArtifactManifest Manifest
    {
        get => _manifest;
        init => _manifest = RequireNonNull(value, nameof(Manifest));
    }
    public VerificationResult Verification
    {
        get => _verification;
        init => _verification = RequireNonNull(value, nameof(Verification));
    }
    public SandboxCompiledEntrypoint Entrypoint
    {
        get => _entrypoint;
        init => _entrypoint = RequireNonNull(value, nameof(Entrypoint));
    }
    public CompiledRuntimeFormKind RuntimeForm
    {
        get => _runtimeForm;
        init => _runtimeForm = RequireDefined(
            value,
            nameof(RuntimeForm),
            "Compiled runtime form is not supported.");
    }
    public CompiledCacheStatus CacheStatus
    {
        get => _cacheStatus;
        init => _cacheStatus = RequireDefined(
            value,
            nameof(CacheStatus),
            "Compiled cache status is not supported.");
    }
    public string? CacheInvalidReason { get; init; }
    public string ArtifactHash => AssemblyHash;

    private static T RequireNonNull<T>(T? value, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    private static TEnum RequireDefined<TEnum>(TEnum value, string paramName, string message)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, message);
        }

        return value;
    }
}

public interface ISandboxCompiler
{
    ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken);
}
