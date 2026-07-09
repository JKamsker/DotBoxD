using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxErrorContractTests
{
    [Fact]
    public void Runtime_exception_rejects_null_error_argument()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new SandboxRuntimeException(null!));

        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void Runtime_exception_message_uses_valid_safe_message()
    {
        var error = new SandboxError(SandboxErrorCode.InvalidInput, "safe terminal message");

        var exception = new SandboxRuntimeException(error);

        Assert.Same(error, exception.Error);
        Assert.Equal("safe terminal message", exception.Message);
    }

    [Theory]
    [MemberData(nameof(MalformedErrors))]
    public void Sandbox_error_rejects_malformed_public_contract_values(
        SandboxErrorCode code,
        string? safeMessage,
        Type expectedExceptionType,
        string expectedParamName)
    {
        var exception = Record.Exception(() => new SandboxError(code, safeMessage!));

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.IsType(expectedExceptionType, argumentException);
        Assert.Equal(expectedParamName, argumentException.ParamName);
    }

    [Fact]
    public void Sandbox_error_rejects_malformed_code_init_value()
    {
        var error = new SandboxError(SandboxErrorCode.InvalidInput, "safe message");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = error with { Code = (SandboxErrorCode)999 });

        Assert.Equal("Code", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(MalformedSafeMessages))]
    public void Sandbox_error_rejects_malformed_safe_message_init_value(
        string? safeMessage,
        Type expectedExceptionType)
    {
        var error = new SandboxError(SandboxErrorCode.InvalidInput, "safe message");

        var exception = Record.Exception(() => _ = error with { SafeMessage = safeMessage! });

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.IsType(expectedExceptionType, argumentException);
        Assert.Equal("SafeMessage", argumentException.ParamName);
    }

    public static TheoryData<SandboxErrorCode, string?, Type, string> MalformedErrors()
        => new()
        {
            { (SandboxErrorCode)999, "safe message", typeof(ArgumentOutOfRangeException), "Code" },
            { SandboxErrorCode.InvalidInput, null, typeof(ArgumentNullException), "SafeMessage" },
            { SandboxErrorCode.InvalidInput, string.Empty, typeof(ArgumentException), "SafeMessage" },
            { SandboxErrorCode.InvalidInput, "   ", typeof(ArgumentException), "SafeMessage" }
        };

    public static TheoryData<string?, Type> MalformedSafeMessages()
        => new()
        {
            { null, typeof(ArgumentNullException) },
            { string.Empty, typeof(ArgumentException) },
            { "\t", typeof(ArgumentException) }
        };
}
