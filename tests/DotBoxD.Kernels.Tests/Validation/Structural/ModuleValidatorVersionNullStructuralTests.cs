using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorVersionNullStructuralTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [MemberData(nameof(NullVersionModules))]
    public void ModuleValidator_reports_null_module_version_fields(
        string scenario,
        SandboxModule module,
        string[] expectedTerms)
    {
        ModuleValidationResult? result = null;

        var exception = Record.Exception(() => result = Validate(module));

        Assert.True(exception is null, $"{scenario}: {exception}");
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => DiagnosticMentions(d, expectedTerms));
    }

    public static TheoryData<string, SandboxModule, string[]> NullVersionModules()
        => new()
        {
            {
                "module version",
                Module(version: null!, targetSandboxVersion: SandboxLanguage.CurrentVersion),
                ["version", "null"]
            },
            {
                "target sandbox version",
                Module(version: SemVersion.One, targetSandboxVersion: null!),
                ["target", "version", "null"]
            }
        };

    private static ModuleValidationResult Validate(SandboxModule module)
        => new ModuleValidator().Validate(module, new BindingRegistry([]));

    private static SandboxModule Module(SemVersion version, SemVersion targetSandboxVersion)
        => new(
            "module-validator-version-null-validation",
            version,
            targetSandboxVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
            ],
            new Dictionary<string, string>());

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
