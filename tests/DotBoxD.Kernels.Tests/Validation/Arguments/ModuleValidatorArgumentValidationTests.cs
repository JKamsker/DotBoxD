using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorArgumentValidationTests
{
    [Fact]
    public void Validate_rejects_null_module_argument()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ModuleValidator().Validate(null!, new BindingRegistry([])));

        Assert.Equal("module", exception.ParamName);
    }

    [Fact]
    public void Validate_rejects_null_bindings_argument()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ModuleValidator().Validate(PureModule(), null!));

        Assert.Equal("bindings", exception.ParamName);
    }

    private static SandboxModule PureModule()
        => new(
            "module-validator-arguments",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)), new SourceSpan(0, 0))])
            ],
            new Dictionary<string, string>());
}
