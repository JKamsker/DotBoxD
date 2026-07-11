using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Diagnostics;

/// <summary>
/// Public reference entry for a single runtime plugin diagnostic
/// <see cref="SandboxDiagnostic.Code"/> emitted by the <c>DotBoxD.Plugins</c> package.
/// </summary>
/// <param name="Code">The stable <c>DBXK*</c> diagnostic code.</param>
/// <param name="Phase">The runtime phase that emits the code.</param>
/// <param name="Audience">Who must act on the diagnostic.</param>
/// <param name="Meaning">A short human-readable description of the rule that was violated.</param>
/// <param name="LikelyCause">The most common reason this code is emitted.</param>
/// <param name="Remediation">Guidance for the plugin author or host operator investigating the code.</param>
public sealed record PluginDiagnosticReference(
    string Code,
    PluginDiagnosticPhase Phase,
    PluginDiagnosticAudience Audience,
    string Meaning,
    string LikelyCause,
    string Remediation)
{
    private string _code = ValidateRequired(Code, nameof(Code));
    private PluginDiagnosticPhase _phase = ValidatePhase(Phase);
    private PluginDiagnosticAudience _audience = ValidateAudience(Audience);
    private string _meaning = ValidateRequired(Meaning, nameof(Meaning));
    private string _likelyCause = ValidateRequired(LikelyCause, nameof(LikelyCause));
    private string _remediation = ValidateRequired(Remediation, nameof(Remediation));

    /// <summary>The stable <c>DBXK*</c> diagnostic code.</summary>
    public string Code
    {
        get => _code;
        init => _code = ValidateRequired(value, nameof(Code));
    }

    /// <summary>The runtime phase that emits the code.</summary>
    public PluginDiagnosticPhase Phase
    {
        get => _phase;
        init => _phase = ValidatePhase(value);
    }

    /// <summary>Who must act on the diagnostic.</summary>
    public PluginDiagnosticAudience Audience
    {
        get => _audience;
        init => _audience = ValidateAudience(value);
    }

    /// <summary>A short human-readable description of the rule that was violated.</summary>
    public string Meaning
    {
        get => _meaning;
        init => _meaning = ValidateRequired(value, nameof(Meaning));
    }

    /// <summary>The most common reason this code is emitted.</summary>
    public string LikelyCause
    {
        get => _likelyCause;
        init => _likelyCause = ValidateRequired(value, nameof(LikelyCause));
    }

    /// <summary>Guidance for the plugin author or host operator investigating the code.</summary>
    public string Remediation
    {
        get => _remediation;
        init => _remediation = ValidateRequired(value, nameof(Remediation));
    }

    private static string ValidateRequired(string? value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    private static PluginDiagnosticPhase ValidatePhase(PluginDiagnosticPhase value) =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentException("Plugin diagnostic phase must be defined.", nameof(Phase));

    private static PluginDiagnosticAudience ValidateAudience(PluginDiagnosticAudience value) =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentException("Plugin diagnostic audience must be defined.", nameof(Audience));
}
