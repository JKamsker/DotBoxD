using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Compiler;

using DotBoxD.Kernels;

public delegate SandboxValue SandboxCompiledEntrypoint(SandboxContext context, SandboxValue input);

public sealed record CompileOptions(string Entrypoint, bool Optimize = false)
{
    private string _entrypoint =
        Entrypoint ?? throw new ArgumentNullException(nameof(Entrypoint));

    public string Entrypoint
    {
        get => _entrypoint;
        init => _entrypoint = value ?? throw new ArgumentNullException(nameof(Entrypoint));
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
