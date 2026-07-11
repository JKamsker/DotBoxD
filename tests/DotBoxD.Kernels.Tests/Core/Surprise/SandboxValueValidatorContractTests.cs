using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxValueValidatorContractTests
{
    [Theory]
    [MemberData(nameof(NullValidatorArguments))]
    public void RequireType_rejects_null_public_arguments(Action validate, string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentNullException>(validate);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NullEntrypointBinderArguments))]
    public void EntrypointBinder_RequireType_rejects_null_public_arguments(
        Action validate,
        string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentNullException>(validate);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    public static TheoryData<Action, string> NullValidatorArguments()
    {
        var value = SandboxValue.FromInt32(1);

        return new TheoryData<Action, string>
        {
            {
                () => SandboxValueValidator.RequireType(null!, SandboxType.I32, "bad input"),
                "value"
            },
            {
                () => SandboxValueValidator.RequireType(value, null!, "bad input"),
                "expectedType"
            },
            {
                () => SandboxValueValidator.RequireType(value, SandboxType.I32, null!),
                "message"
            }
        };
    }

    public static TheoryData<Action, string> NullEntrypointBinderArguments()
    {
        var value = SandboxValue.FromInt32(1);

        return new TheoryData<Action, string>
        {
            {
                () => EntrypointBinder.RequireType(null!, SandboxType.I32, "bad input"),
                "value"
            },
            {
                () => EntrypointBinder.RequireType(value, null!, "bad input"),
                "expectedType"
            },
            {
                () => EntrypointBinder.RequireType(value, SandboxType.I32, null!),
                "message"
            }
        };
    }
}
