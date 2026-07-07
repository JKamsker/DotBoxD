using System.Text.RegularExpressions;
using DotBoxD.Plugins.Runtime.Diagnostics;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

/// <summary>
/// Regression coverage for API-0019: the <c>DotBoxD.Plugins</c> package emits stable runtime
/// <c>SGP*</c> diagnostics from package install, prepared-package validation, kernel-entrypoint
/// checks, and live-setting validation. The public <see cref="PluginDiagnosticCodes"/> reference
/// must document every runtime code so hosts and upload UIs can triage failures instead of seeing
/// opaque codes, and so new runtime diagnostics cannot ship without user-facing guidance.
/// </summary>
public sealed class Fix_API_0019_Tests
{
    private const string ReferenceFileName = "PluginDiagnosticCodes.cs";

    private static readonly Regex EmittedCodePattern = new(
        "\"(?<code>DBXK[0-9]+)\"",
        RegexOptions.Compiled);

    [Fact]
    public void Public_reference_documents_every_emitted_runtime_code()
    {
        var emitted = EmittedRuntimePluginCodes();
        var documented = PluginDiagnosticCodes.All
            .Select(reference => reference.Code)
            .ToHashSet(StringComparer.Ordinal);

        var undocumented = emitted
            .Where(code => !documented.Contains(code))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            undocumented.Length == 0,
            $"DotBoxD.Plugins emits runtime codes with no public reference entry: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void Public_reference_has_no_codes_that_the_runtime_never_emits()
    {
        var emitted = EmittedRuntimePluginCodes();

        var orphaned = PluginDiagnosticCodes.All
            .Select(reference => reference.Code)
            .Where(code => !emitted.Contains(code))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphaned.Length == 0,
            $"Public reference lists codes the runtime never emits: {string.Join(", ", orphaned)}");
    }

    [Fact]
    public void Public_reference_entries_are_well_formed_and_unique()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in PluginDiagnosticCodes.All)
        {
            Assert.StartsWith("DBXK", reference.Code, StringComparison.Ordinal);
            Assert.True(codes.Add(reference.Code), $"Duplicate reference entry for {reference.Code}");
            Assert.False(string.IsNullOrWhiteSpace(reference.Meaning));
            Assert.False(string.IsNullOrWhiteSpace(reference.LikelyCause));
            Assert.False(string.IsNullOrWhiteSpace(reference.Remediation));
            Assert.True(Enum.IsDefined(reference.Phase));
            Assert.True(Enum.IsDefined(reference.Audience));
        }
    }

    [Theory]
    [InlineData(null, "Meaning", "LikelyCause", "Remediation", "Code")]
    [InlineData("DBXK999", null, "LikelyCause", "Remediation", "Meaning")]
    [InlineData("DBXK999", "Meaning", null, "Remediation", "LikelyCause")]
    [InlineData("DBXK999", "Meaning", "LikelyCause", null, "Remediation")]
    public void Public_reference_rejects_null_required_text(
        string? code,
        string? meaning,
        string? likelyCause,
        string? remediation,
        string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new PluginDiagnosticReference(
                code!,
                PluginDiagnosticPhase.PackageValidation,
                PluginDiagnosticAudience.PluginAuthor,
                meaning!,
                likelyCause!,
                remediation!));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [InlineData((PluginDiagnosticPhase)(-1))]
    [InlineData((PluginDiagnosticPhase)999)]
    public void Public_reference_rejects_undefined_phase(PluginDiagnosticPhase phase)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new PluginDiagnosticReference(
                "DBXK999",
                phase,
                PluginDiagnosticAudience.PluginAuthor,
                "Meaning",
                "LikelyCause",
                "Remediation"));

        Assert.Equal("Phase", exception.ParamName);
    }

    [Theory]
    [InlineData((PluginDiagnosticAudience)(-1))]
    [InlineData((PluginDiagnosticAudience)999)]
    public void Public_reference_rejects_undefined_audience(PluginDiagnosticAudience audience)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new PluginDiagnosticReference(
                "DBXK999",
                PluginDiagnosticPhase.PackageValidation,
                audience,
                "Meaning",
                "LikelyCause",
                "Remediation"));

        Assert.Equal("Audience", exception.ParamName);
    }

    [Fact]
    public void TryGetReference_round_trips_known_codes()
    {
        Assert.True(PluginDiagnosticCodes.TryGetReference("DBXK010", out var reference));
        Assert.Equal("DBXK010", reference.Code);
        Assert.Equal(PluginDiagnosticPhase.PackageValidation, reference.Phase);
        Assert.Equal(PluginDiagnosticAudience.PluginAuthor, reference.Audience);
    }

    [Fact]
    public void TryGetReference_classifies_live_setting_range_as_host_operator_fixable()
    {
        Assert.True(PluginDiagnosticCodes.TryGetReference("DBXK023", out var reference));
        Assert.Equal(PluginDiagnosticPhase.LiveSetting, reference.Phase);
        Assert.Equal(PluginDiagnosticAudience.HostOperator, reference.Audience);
    }

    [Fact]
    public void TryGetReference_fails_closed_for_unknown_codes()
    {
        Assert.False(PluginDiagnosticCodes.TryGetReference("DBXK999", out var reference));
        Assert.Equal("DBXK999", reference.Code);
        Assert.False(string.IsNullOrWhiteSpace(reference.Remediation));
    }

    private static HashSet<string> EmittedRuntimePluginCodes()
    {
        var pluginsRoot = Path.Combine(RepositoryRoot(), "src", "Hosting", "DotBoxD.Plugins");
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(pluginsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.GetFileName(file).Equals(ReferenceFileName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in EmittedCodePattern.Matches(File.ReadAllText(file)))
            {
                codes.Add(match.Groups["code"].Value);
            }
        }

        Assert.NotEmpty(codes);
        return codes;
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
