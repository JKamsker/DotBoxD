using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Serialization;

public sealed class JsonExporterConstrainedScalarLiteralTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Theory]
    [MemberData(nameof(InvalidConstrainedScalarLiterals))]
    public void Export_rejects_or_round_trips_invalid_constrained_scalar_literals(
        SandboxType returnType,
        SandboxValue value,
        string expectedDiagnostic)
    {
        var module = ModuleReturning(returnType, value);
        string? json = null;
        var exportError = Record.Exception(() => json = JsonExporter.Export(module));
        if (exportError is not null)
        {
            var validation = Assert.IsType<SandboxValidationException>(exportError);
            Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == expectedDiagnostic);
            return;
        }

        SandboxModule? roundTrip = null;
        var importError = Record.Exception(() => roundTrip = JsonImporter.Import(json!));
        if (importError is not null)
        {
            var validation = Assert.IsType<SandboxValidationException>(importError);
            Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == expectedDiagnostic);
            return;
        }

        var returned = ReturnedLiteral(roundTrip!);
        AssertConstrainedScalarEqual(value, returned);
    }

    public static TheoryData<SandboxType, SandboxValue, string> InvalidConstrainedScalarLiterals()
        => new()
        {
            {
                SandboxType.SandboxPath,
                new SandboxPathValue(new SandboxPath("../secret.txt")),
                "E-JSON-PATH"
            },
            {
                SandboxType.SandboxUri,
                new SandboxUriValue(new SandboxUri("https://user:pass@example.com/config")),
                "E-JSON-URI"
            },
            {
                SandboxType.Scalar("PluginId"),
                new OpaqueIdValue("PluginId", "../secret"),
                "E-JSON-ID"
            }
        };

    private static SandboxModule ModuleReturning(SandboxType returnType, SandboxValue value)
        => new(
            "constrained-scalar-exporter",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "ConstrainedScalar",
                    true,
                    [],
                    returnType,
                    [new ReturnStatement(new LiteralExpression(value, Span), Span)])
            ],
            new Dictionary<string, string>());

    private static SandboxValue ReturnedLiteral(SandboxModule module)
    {
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(Assert.Single(module.Functions).Body));
        return Assert.IsType<LiteralExpression>(ret.Value).Value;
    }

    private static void AssertConstrainedScalarEqual(SandboxValue expected, SandboxValue actual)
    {
        switch (expected)
        {
            case SandboxPathValue path:
                Assert.Equal(path.Value.RelativePath, Assert.IsType<SandboxPathValue>(actual).Value.RelativePath);
                break;
            case SandboxUriValue uri:
                Assert.Equal(uri.Value.Value, Assert.IsType<SandboxUriValue>(actual).Value.Value);
                break;
            case OpaqueIdValue id:
                var actualId = Assert.IsType<OpaqueIdValue>(actual);
                Assert.Equal(id.TypeName, actualId.TypeName);
                Assert.Equal(id.Value, actualId.Value);
                break;
            default:
                throw new InvalidOperationException("Expected a constrained scalar value.");
        }
    }
}
