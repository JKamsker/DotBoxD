using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorFunctionNullTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void Validate_reports_null_module_function_entries()
    {
        ModuleValidationResult? result = null;

        var exception = Record.Exception(() =>
            result = new ModuleValidator().Validate(ModuleWithNullFunction(), new BindingRegistry([])));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "E-STRUCT-NULL" &&
            diagnostic.Message.Contains("functions", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    private static SandboxModule ModuleWithNullFunction()
        => new(
            "module-validator-function-null",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                null!,
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
            ],
            new Dictionary<string, string>());
}
