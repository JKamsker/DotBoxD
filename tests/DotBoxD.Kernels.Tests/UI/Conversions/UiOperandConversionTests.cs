using DotBoxD.Kernels.Tests.UI.Authoring;

namespace DotBoxD.Kernels.Tests.UI.Conversions;

public sealed class UiOperandConversionTests
{
    [Theory]
    [InlineData("=> new ConversionBox(value) == new ConversionBox(value + 2);")]
    [InlineData("=> new ConversionBox(value) != new ConversionBox(value + 2);")]
    [InlineData("=> new ConversionBox(value) < new ConversionBox(value + 2);")]
    [InlineData("=> new ConversionBox(value) <= new ConversionBox(value + 2);")]
    [InlineData("=> new ConversionBox(value) > new ConversionBox(value + 2);")]
    [InlineData("=> new ConversionBox(value) >= new ConversionBox(value + 2);")]
    [InlineData("=> -new ConversionBox(value) == 0;")]
    [InlineData("=> +new ConversionBox(value) == 0;")]
    [InlineData("=> MakeBox(value) == MakeBox(value + 2);")]
    [InlineData("=> (int)new ConversionBox(value) == 0;")]
    [InlineData("=> Positive(new ConversionBox(value));")]
    [InlineData("=> Converted(value) == 0;")]
    [InlineData("{ var box = new RoundTripBox(value); box += 1; return true; }")]
    public void User_defined_operator_operand_conversions_fail_closed(string body)
        => UiBooleanGeneratorFixture.AssertUnsupported(body);

    [Theory]
    [InlineData("{ int left = value; long right = 1; return left + right > 0; }", ExecutionMode.Interpreted)]
    [InlineData("{ int left = value; long right = 1; return left + right > 0; }", ExecutionMode.Compiled)]
    [InlineData("{ int left = value; double right = 1.5; return left + right == 1.5; }", ExecutionMode.Interpreted)]
    [InlineData("{ int left = value; double right = 1.5; return left + right == 1.5; }", ExecutionMode.Compiled)]
    [InlineData("{ byte small = 2; int total = value; return small + total == 2; }", ExecutionMode.Interpreted)]
    [InlineData("{ byte small = 2; int total = value; return small + total == 2; }", ExecutionMode.Compiled)]
    public Task Built_in_operator_operand_widening_preserves_both_kernel_modes(string body, ExecutionMode mode)
        => UiBooleanGeneratorFixture.AssertExecutes(body, true, mode);
}
