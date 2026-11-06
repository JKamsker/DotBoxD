using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Validation.Structural;

// A module function whose id equals a built-in intrinsic name is resolved inconsistently across
// backends (the validator and boxed paths pick the intrinsic, inline fast paths pick the local
// function), so validation rejects the declaration outright. Shadowing a host catalog binding stays
// allowed: user functions shadow same-named bindings by design (see BindingResolutionTests).
public sealed class ModuleValidatorFunctionShadowingTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [InlineData("list.count")]
    [InlineData("list.empty")]
    [InlineData("map.get")]
    [InlineData("record.new")]
    [InlineData("record.get")]
    [InlineData("numeric.toI64")]
    [InlineData("numeric.toF64")]
    public void Validate_rejects_function_id_that_shadows_an_intrinsic(string functionId)
    {
        var result = new ModuleValidator().Validate(ModuleWithHelper(functionId), new BindingRegistry([]));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "E-STRUCT-FN-SHADOW" &&
            d.Message.Contains(functionId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("helpers.twice")]
    [InlineData("math.sqrt")]
    public void Validate_accepts_dotted_function_id_that_is_not_an_intrinsic(string functionId)
    {
        var bindings = new BindingRegistryBuilder().AddDefaultPureBindings().Build();

        var result = new ModuleValidator().Validate(ModuleWithHelper(functionId), bindings);

        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static SandboxModule ModuleWithHelper(string helperId)
        => new(
            "function-shadowing",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    helperId,
                    IsEntrypoint: false,
                    [new Parameter("value", SandboxType.F64)],
                    SandboxType.F64,
                    [new ReturnStatement(new VariableExpression("value", Span), Span)]),
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.F64,
                    [
                        new ReturnStatement(
                            new CallExpression(
                                helperId,
                                [new LiteralExpression(SandboxValue.FromDouble(4.0), Span)],
                                null,
                                Span),
                            Span)
                    ])
            ],
            new Dictionary<string, string>());
}
