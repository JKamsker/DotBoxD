using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorMetadataNullTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_reports_null_metadata_values()
    {
        var module = ValidModule(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pluginId"] = null!
        });
        ModuleValidationResult? result = null;

        var exception = Record.Exception(() => result = new ModuleValidator().Validate(module, new BindingRegistry([])));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "E-STRUCT-NULL"
                && DiagnosticMentions(diagnostic, ["metadata", "pluginId", "null"]));
    }

    private static SandboxModule ValidModule(IReadOnlyDictionary<string, string> metadata)
        => new(
            "module-validator-metadata-null",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
            ],
            metadata);

    private static bool DiagnosticMentions(SandboxDiagnostic diagnostic, IReadOnlyList<string> expectedTerms)
    {
        for (var i = 0; i < expectedTerms.Count; i++)
        {
            if (!diagnostic.Message.Contains(expectedTerms[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
