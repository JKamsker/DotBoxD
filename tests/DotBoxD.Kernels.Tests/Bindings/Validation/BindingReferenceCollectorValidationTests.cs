using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings.Validation;

public sealed class BindingReferenceCollectorValidationTests
{
    [Fact]
    public void Collect_rejects_null_module_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.Collect(null!, EmptyBindings()));

        Assert.Equal("module", ex.ParamName);
    }

    [Fact]
    public void CollectByFunction_rejects_null_module_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.CollectByFunction(null!, EmptyBindings()));

        Assert.Equal("module", ex.ParamName);
    }

    [Fact]
    public void Collect_rejects_null_bindings_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.Collect(PureModule(), null!));

        Assert.Equal("bindings", ex.ParamName);
    }

    [Fact]
    public void CollectByFunction_rejects_null_bindings_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.CollectByFunction(PureModule(), null!));

        Assert.Equal("bindings", ex.ParamName);
    }

    private static BindingRegistry EmptyBindings() => new([]);

    private static SandboxModule PureModule()
        => new(
            "pure-module",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [
                        new ReturnStatement(
                            new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());
}
