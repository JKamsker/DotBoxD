using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorFunctionReturnTypeNullTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_reports_null_function_return_type()
    {
        var module = new SandboxModule(
            "module-validator-null-return-type",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    ReturnType: null!,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
            ],
            new Dictionary<string, string>());

        ModuleValidationResult? result = null;

        var exception = Record.Exception(() =>
            result = new ModuleValidator().Validate(module, new BindingRegistry([])));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "E-STRUCT-NULL" &&
            DiagnosticMentions(d, "main", "return", "null"));
    }

    private static bool DiagnosticMentions(SandboxDiagnostic diagnostic, params string[] expectedTerms)
    {
        for (var i = 0; i < expectedTerms.Length; i++)
        {
            if (!diagnostic.Message.Contains(expectedTerms[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
