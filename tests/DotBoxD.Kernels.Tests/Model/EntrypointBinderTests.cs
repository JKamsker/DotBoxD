using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Model;

public sealed class EntrypointBinderTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [MemberData(nameof(NullFacadeArguments))]
    public void Public_facades_reject_null_arguments_at_their_boundary(
        Action bind,
        string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentNullException>(bind);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void BindArguments_reuses_empty_array_for_zero_parameter_entrypoints()
    {
        var arguments = EntrypointBinder.BindArguments(ZeroParameterFunction(), SandboxValue.Unit);

        Assert.Same(Array.Empty<SandboxValue>(), arguments);
    }

    public static TheoryData<Action, string> NullFacadeArguments()
        => new()
        {
            {
                () => EntrypointBinder.BindArguments(null!, SandboxValue.Unit),
                "function"
            },
            {
                () => EntrypointBinder.BindArguments(ZeroParameterFunction(), null!),
                "input"
            },
            {
                () => EntrypointBinder.BindArguments(OneParameterFunction(), null!),
                "input"
            },
            {
                () => EntrypointBinder.ValidateInputShape(null!, parameterCount: 0),
                "input"
            },
            {
                () => EntrypointBinder.ValidateInputShape(null!, parameterCount: 1),
                "input"
            },
            {
                () => EntrypointBinder.GetArgument(null!, index: 0, parameterCount: 1, SandboxType.I32),
                "input"
            }
        };

    private static SandboxFunction ZeroParameterFunction()
        => new(
            "main",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);

    private static SandboxFunction OneParameterFunction()
        => new(
            "main",
            IsEntrypoint: true,
            [new Parameter("value", SandboxType.I32)],
            SandboxType.I32,
            [new ReturnStatement(new VariableExpression("value", Span), Span)]);
}
