using DotBoxD.Kernels.Verifier.Diagnostics;

namespace DotBoxD.Kernels.Tests.Hosting.Regression.PublicContracts;

public sealed class VerifierDiagnosticReferenceContractTests
{
    private const string ValidCode = "V-TEST";
    private const string ValidMeaning = "Meaning";
    private const string ValidLikelyCause = "Likely cause";
    private const string ValidRemediation = "Remediation";

    public static TheoryData<string, string> BlankRequiredTextCases() =>
        new()
        {
            { nameof(VerifierDiagnosticReference.Code), string.Empty },
            { nameof(VerifierDiagnosticReference.Code), "   " },
            { nameof(VerifierDiagnosticReference.Meaning), string.Empty },
            { nameof(VerifierDiagnosticReference.Meaning), " " },
            { nameof(VerifierDiagnosticReference.LikelyCause), string.Empty },
            { nameof(VerifierDiagnosticReference.LikelyCause), "\t" },
            { nameof(VerifierDiagnosticReference.Remediation), string.Empty },
            { nameof(VerifierDiagnosticReference.Remediation), "\n" },
        };

    [Theory]
    [MemberData(nameof(BlankRequiredTextCases))]
    public void Public_reference_rejects_blank_required_text_on_construction(
        string fieldName,
        string value)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            _ = new VerifierDiagnosticReference(
                ValueFor(fieldName, nameof(VerifierDiagnosticReference.Code), value, ValidCode),
                VerifierDiagnosticCategory.MalformedArtifact,
                ValueFor(fieldName, nameof(VerifierDiagnosticReference.Meaning), value, ValidMeaning),
                ValueFor(fieldName, nameof(VerifierDiagnosticReference.LikelyCause), value, ValidLikelyCause),
                ValueFor(fieldName, nameof(VerifierDiagnosticReference.Remediation), value, ValidRemediation),
                ExpectedFromCompilerOutput: false));

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
                nameof(VerifierDiagnosticReference.Code) => reference with { Code = value },
                nameof(VerifierDiagnosticReference.Meaning) => reference with { Meaning = value },
                nameof(VerifierDiagnosticReference.LikelyCause) => reference with { LikelyCause = value },
                nameof(VerifierDiagnosticReference.Remediation) => reference with { Remediation = value },
                _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null),
            });

        Assert.Equal(fieldName, exception.ParamName);
    }

    [Fact]
    public void Public_reference_accepts_valid_required_text()
    {
        var reference = ValidReference();

        Assert.Equal(ValidCode, reference.Code);
        Assert.Equal(VerifierDiagnosticCategory.MalformedArtifact, reference.Category);
        Assert.Equal(ValidMeaning, reference.Meaning);
        Assert.Equal(ValidLikelyCause, reference.LikelyCause);
        Assert.Equal(ValidRemediation, reference.Remediation);
        Assert.False(reference.ExpectedFromCompilerOutput);
    }

    private static VerifierDiagnosticReference ValidReference() =>
        new(
            ValidCode,
            VerifierDiagnosticCategory.MalformedArtifact,
            ValidMeaning,
            ValidLikelyCause,
            ValidRemediation,
            ExpectedFromCompilerOutput: false);

    private static string ValueFor(string actualFieldName, string expectedFieldName, string value, string fallback) =>
        string.Equals(actualFieldName, expectedFieldName, StringComparison.Ordinal) ? value : fallback;
}
