using System.Collections.ObjectModel;

namespace DotBoxD.Kernels.Verifier.Generated;

public sealed record ArtifactManifest(
    int ArtifactVersion,
    string CacheKey,
    string ModuleHash,
    string PlanHash,
    string PolicyHash,
    string BindingManifestHash,
    string RuntimeFacadeHash,
    string CompilerVersion,
    string TypeSystemVersion,
    string EffectAnalysisVersion,
    string VerifierVersion,
    string LanguageVersion,
    string TargetFramework,
    IReadOnlyList<string> OptimizationFlags,
    string AssemblyHash,
    DateTimeOffset CreatedAt)
{
    private string _cacheKey = VerificationModelCopy.Required(CacheKey, nameof(CacheKey));
    private string _moduleHash = VerificationModelCopy.Required(ModuleHash, nameof(ModuleHash));
    private string _planHash = VerificationModelCopy.Required(PlanHash, nameof(PlanHash));
    private string _policyHash = VerificationModelCopy.Required(PolicyHash, nameof(PolicyHash));
    private string _bindingManifestHash = VerificationModelCopy.Required(BindingManifestHash, nameof(BindingManifestHash));
    private string _runtimeFacadeHash = VerificationModelCopy.Required(RuntimeFacadeHash, nameof(RuntimeFacadeHash));
    private string _compilerVersion = VerificationModelCopy.Required(CompilerVersion, nameof(CompilerVersion));
    private string _typeSystemVersion = VerificationModelCopy.Required(TypeSystemVersion, nameof(TypeSystemVersion));
    private string _effectAnalysisVersion = VerificationModelCopy.Required(EffectAnalysisVersion, nameof(EffectAnalysisVersion));
    private string _verifierVersion = VerificationModelCopy.Required(VerifierVersion, nameof(VerifierVersion));
    private string _languageVersion = VerificationModelCopy.Required(LanguageVersion, nameof(LanguageVersion));
    private string _targetFramework = VerificationModelCopy.Required(TargetFramework, nameof(TargetFramework));
    private IReadOnlyList<string> _optimizationFlags = VerificationModelCopy.List(
        OptimizationFlags,
        nameof(OptimizationFlags));
    private string _assemblyHash = VerificationModelCopy.Required(AssemblyHash, nameof(AssemblyHash));

    public string CacheKey
    {
        get => _cacheKey;
        init => _cacheKey = VerificationModelCopy.Required(value, nameof(CacheKey));
    }

    public string ModuleHash
    {
        get => _moduleHash;
        init => _moduleHash = VerificationModelCopy.Required(value, nameof(ModuleHash));
    }

    public string PlanHash
    {
        get => _planHash;
        init => _planHash = VerificationModelCopy.Required(value, nameof(PlanHash));
    }

    public string PolicyHash
    {
        get => _policyHash;
        init => _policyHash = VerificationModelCopy.Required(value, nameof(PolicyHash));
    }

    public string BindingManifestHash
    {
        get => _bindingManifestHash;
        init => _bindingManifestHash = VerificationModelCopy.Required(value, nameof(BindingManifestHash));
    }

    public string RuntimeFacadeHash
    {
        get => _runtimeFacadeHash;
        init => _runtimeFacadeHash = VerificationModelCopy.Required(value, nameof(RuntimeFacadeHash));
    }

    public string CompilerVersion
    {
        get => _compilerVersion;
        init => _compilerVersion = VerificationModelCopy.Required(value, nameof(CompilerVersion));
    }

    public string TypeSystemVersion
    {
        get => _typeSystemVersion;
        init => _typeSystemVersion = VerificationModelCopy.Required(value, nameof(TypeSystemVersion));
    }

    public string EffectAnalysisVersion
    {
        get => _effectAnalysisVersion;
        init => _effectAnalysisVersion = VerificationModelCopy.Required(value, nameof(EffectAnalysisVersion));
    }

    public string VerifierVersion
    {
        get => _verifierVersion;
        init => _verifierVersion = VerificationModelCopy.Required(value, nameof(VerifierVersion));
    }

    public string LanguageVersion
    {
        get => _languageVersion;
        init => _languageVersion = VerificationModelCopy.Required(value, nameof(LanguageVersion));
    }

    public string TargetFramework
    {
        get => _targetFramework;
        init => _targetFramework = VerificationModelCopy.Required(value, nameof(TargetFramework));
    }

    public IReadOnlyList<string> OptimizationFlags
    {
        get => _optimizationFlags;
        init => _optimizationFlags = VerificationModelCopy.List(value, nameof(OptimizationFlags));
    }

    public string AssemblyHash
    {
        get => _assemblyHash;
        init => _assemblyHash = VerificationModelCopy.Required(value, nameof(AssemblyHash));
    }
}

public sealed record VerificationManifestIdentity(
    int? ArtifactVersion = null,
    string? CacheKey = null,
    string? ModuleHash = null,
    string? PlanHash = null,
    string? PolicyHash = null,
    string? BindingManifestHash = null,
    string? RuntimeFacadeHash = null,
    string? CompilerVersion = null,
    string? TypeSystemVersion = null,
    string? EffectAnalysisVersion = null,
    string? VerifierVersion = null,
    string? LanguageVersion = null,
    string? TargetFramework = null,
    IReadOnlyList<string>? OptimizationFlags = null)
{
    private IReadOnlyList<string>? _optimizationFlags = VerificationModelCopy.NullableList(
        OptimizationFlags,
        nameof(OptimizationFlags));

    public IReadOnlyList<string>? OptimizationFlags
    {
        get => _optimizationFlags;
        init => _optimizationFlags = VerificationModelCopy.NullableList(value, nameof(OptimizationFlags));
    }

    public static VerificationManifestIdentity FromManifest(ArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return new(
            manifest.ArtifactVersion,
            manifest.CacheKey,
            manifest.ModuleHash,
            manifest.PlanHash,
            manifest.PolicyHash,
            manifest.BindingManifestHash,
            manifest.RuntimeFacadeHash,
            manifest.CompilerVersion,
            manifest.TypeSystemVersion,
            manifest.EffectAnalysisVersion,
            manifest.VerifierVersion,
            manifest.LanguageVersion,
            manifest.TargetFramework,
            manifest.OptimizationFlags);
    }
}

public sealed record VerificationDiagnostic(string Code, string Message)
{
    private string _code = VerificationModelCopy.RequiredText(Code, nameof(Code));
    private string _message = VerificationModelCopy.RequiredText(Message, nameof(Message));

    public string Code
    {
        get => _code;
        init => _code = VerificationModelCopy.RequiredText(value, nameof(Code));
    }

    public string Message
    {
        get => _message;
        init => _message = VerificationModelCopy.RequiredText(value, nameof(Message));
    }
}

public sealed record VerificationResult(
    bool Succeeded,
    IReadOnlyList<VerificationDiagnostic> Diagnostics,
    string AssemblyHash,
    string VerifierVersion,
    DateTimeOffset VerifiedAt)
{
    private IReadOnlyList<VerificationDiagnostic> _diagnostics = VerificationModelCopy.Diagnostics(
        Diagnostics,
        Succeeded);
    private string _assemblyHash = VerificationModelCopy.Required(AssemblyHash, nameof(AssemblyHash));
    private string _verifierVersion = VerificationModelCopy.Required(VerifierVersion, nameof(VerifierVersion));

    public IReadOnlyList<VerificationDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = VerificationModelCopy.Diagnostics(value, Succeeded);
    }

    public string AssemblyHash
    {
        get => _assemblyHash;
        init => _assemblyHash = VerificationModelCopy.Required(value, nameof(AssemblyHash));
    }

    public string VerifierVersion
    {
        get => _verifierVersion;
        init => _verifierVersion = VerificationModelCopy.Required(value, nameof(VerifierVersion));
    }
}

internal static class VerificationModelCopy
{
    internal static T Required<T>(T? value, string paramName)
        where T : class
        => value ?? throw new ArgumentNullException(paramName);

    internal static string RequiredText(string? value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    internal static IReadOnlyList<T> List<T>(IEnumerable<T> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException("Collection contains a null entry.", paramName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    internal static IReadOnlyList<T>? NullableList<T>(IEnumerable<T>? values, string paramName)
        => values is null ? null : List(values, paramName);

    internal static IReadOnlyList<VerificationDiagnostic> Diagnostics(
        IEnumerable<VerificationDiagnostic> values,
        bool succeeded)
        => RequireNoSuccessDiagnostics(List(values, nameof(VerificationResult.Diagnostics)), succeeded);

    private static IReadOnlyList<VerificationDiagnostic> RequireNoSuccessDiagnostics(
        IReadOnlyList<VerificationDiagnostic> diagnostics,
        bool succeeded)
    {
        if (succeeded && diagnostics.Count > 0)
        {
            throw new ArgumentException(
                "Succeeded verification results cannot include Diagnostics.",
                nameof(VerificationResult.Diagnostics));
        }

        return diagnostics;
    }
}

public interface IGeneratedAssemblyVerifier
{
    ValueTask<VerificationResult> VerifyAsync(
        ReadOnlyMemory<byte> assemblyBytes,
        ArtifactManifest manifest,
        VerificationPolicy policy,
        CancellationToken cancellationToken);
}
