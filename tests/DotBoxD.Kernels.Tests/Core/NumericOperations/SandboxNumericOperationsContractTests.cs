using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxNumericOperationsContractTests
{
    [Theory]
    [MemberData(nameof(NullOperandOperations))]
    public void Numeric_operations_reject_null_operands_at_the_public_boundary(
        Action operation,
        string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentNullException>(operation);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void Numeric_operations_still_report_type_mismatch_for_real_non_numeric_operands()
    {
        var text = SandboxValue.FromString("not-a-number");
        var number = SandboxValue.FromInt32(1);

        var exception = Assert.Throws<SandboxRuntimeException>(
            () => SandboxNumericOperations.Add(text, number));

        Assert.Equal(SandboxErrorCode.InvalidInput, exception.Error.Code);
        Assert.Equal("numeric operand type mismatch", exception.Message);
    }

    public static TheoryData<Action, string> NullOperandOperations()
        => new()
        {
            { () => SandboxNumericOperations.Negate(null!), "value" },
            { () => SandboxNumericOperations.Add(null!, SandboxValue.FromInt32(1)), "left" },
            { () => SandboxNumericOperations.Add(SandboxValue.FromInt32(1), null!), "right" },
            { () => SandboxNumericOperations.LessThan(null!, SandboxValue.FromInt32(1)), "left" }
        };
}
