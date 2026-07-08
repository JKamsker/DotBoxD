using DotBoxD.Plugins.Runtime.Diagnostics;

namespace DotBoxD.Kernels.Tests.Hosting.Regression;

public sealed class PluginDiagnosticReferenceContractTests
{
    private const string ValidCode = "DBXK999";
    private const string ValidMeaning = "Meaning";
    private const string ValidLikelyCause = "Likely cause";
    private const string ValidRemediation = "Remediation";

    public static TheoryData<string, string> BlankRequiredTextCases() =>
        new()
        {
            { nameof(PluginDiagnosticReference.Code), string.Empty },
            { nameof(PluginDiagnosticReference.Code), "   " },
            { nameof(PluginDiagnosticReference.Meaning), string.Empty },
            { nameof(PluginDiagnosticReference.Meaning), " " },
            { nameof(PluginDiagnosticReference.LikelyCause), string.Empty },
            { nameof(PluginDiagnosticReference.LikelyCause), "\t" },
            { nameof(PluginDiagnosticReference.Remediation), string.Empty },
            { nameof(PluginDiagnosticReference.Remediation), "\n" },
        };

    [Theory]
    [MemberData(nameof(BlankRequiredTextCases))]
    public void Public_reference_rejects_blank_required_text_on_construction(
        string fieldName,
        string value)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            _ = new PluginDiagnosticReference(
                ValueFor(fieldName, nameof(PluginDiagnosticReference.Code), value, ValidCode),
                PluginDiagnosticPhase.PackageValidation,
                PluginDiagnosticAudience.PluginAuthor,
                ValueFor(fieldName, nameof(PluginDiagnosticReference.Meaning), value, ValidMeaning),
                ValueFor(fieldName, nameof(PluginDiagnosticReference.LikelyCause), value, ValidLikelyCause),
                ValueFor(fieldName, nameof(PluginDiagnosticReference.Remediation), value, ValidRemediation)));

        Assert.Equal(fieldName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(BlankRequiredTextCases))]
    public void Public_reference_rejects_blank_required_text_on_init(
        string fieldName,
        string value)
    {
        var reference = ValidReference();

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            _ = fieldName switch
            {
                nameof(PluginDiagnosticReference.Code) => reference with { Code = value },
                nameof(PluginDiagnosticReference.Meaning) => reference with { Meaning = value },
                nameof(PluginDiagnosticReference.LikelyCause) => reference with { LikelyCause = value },
                nameof(PluginDiagnosticReference.Remediation) => reference with { Remediation = value },
                _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null),
            });

        Assert.Equal(fieldName, exception.ParamName);
    }

    private static PluginDiagnosticReference ValidReference() =>
        new(
            ValidCode,
            PluginDiagnosticPhase.PackageValidation,
            PluginDiagnosticAudience.PluginAuthor,
            ValidMeaning,
            ValidLikelyCause,
            ValidRemediation);

    private static string ValueFor(string actualFieldName, string expectedFieldName, string value, string fallback) =>
        string.Equals(actualFieldName, expectedFieldName, StringComparison.Ordinal) ? value : fallback;
}
