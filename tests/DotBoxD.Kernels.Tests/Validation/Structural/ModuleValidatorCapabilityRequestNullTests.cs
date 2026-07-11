using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorCapabilityRequestNullTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_reports_null_capability_request_entries()
    {
        var module = new SandboxModule(
            "module-validator-null-capability-request",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [null!],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
            ],
            new Dictionary<string, string>());

        ModuleValidationResult? result = null;

        var exception = Record.Exception(() =>
            result = new ModuleValidator().Validate(module, new BindingRegistry([])));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, IsNullCapabilityRequestDiagnostic);
    }

    private static bool IsNullCapabilityRequestDiagnostic(SandboxDiagnostic diagnostic)
        => diagnostic.Code == "E-STRUCT-NULL"
            && diagnostic.Message.Contains("capabilityRequests", StringComparison.OrdinalIgnoreCase)
            && diagnostic.Message.Contains("null", StringComparison.OrdinalIgnoreCase);
}
