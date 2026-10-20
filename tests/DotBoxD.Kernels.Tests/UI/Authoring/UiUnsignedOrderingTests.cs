namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiUnsignedOrderingTests
{
    [Theory]
    [InlineData("{ var left = UnsignedValue.Max; var right = UnsignedValue.Zero; return left > right; }")]
    [InlineData("{ var left = UnsignedValue.Max; var right = UnsignedValue.Zero; return left >= right; }")]
    [InlineData("{ var left = UnsignedValue.Zero; var right = UnsignedValue.Max; return left < right; }")]
    [InlineData("{ var left = UnsignedValue.Zero; var right = UnsignedValue.Max; return left <= right; }")]
    [InlineData("{ var left = UnsignedValue.Above; var right = UnsignedValue.Below; return left > right; }")]
    [InlineData("{ var left = UnsignedValue.Below; var right = UnsignedValue.Above; return left < right; }")]
    public void Unsigned_enum_ordering_fails_closed_at_compile_time(string body)
        => UiBooleanGeneratorFixture.AssertUnsupported(body);

    [Theory]
    [InlineData("{ var left = UnsignedValue.Max; var right = UnsignedValue.Max; return left == right; }", true, ExecutionMode.Interpreted)]
    [InlineData("{ var left = UnsignedValue.Max; var right = UnsignedValue.Max; return left == right; }", true, ExecutionMode.Compiled)]
    [InlineData("{ var left = UnsignedValue.Max; var right = UnsignedValue.Zero; return left != right; }", true, ExecutionMode.Interpreted)]
    [InlineData("{ var left = UnsignedValue.Max; var right = UnsignedValue.Zero; return left != right; }", true, ExecutionMode.Compiled)]
    [InlineData("{ var left = UnsignedValue.Above; var right = UnsignedValue.Below; return left == right; }", false, ExecutionMode.Interpreted)]
    [InlineData("{ var left = UnsignedValue.Above; var right = UnsignedValue.Below; return left == right; }", false, ExecutionMode.Compiled)]
    [InlineData("{ var left = SignedValue.Negative; var right = SignedValue.Zero; return left < right; }", true, ExecutionMode.Interpreted)]
    [InlineData("{ var left = SignedValue.Negative; var right = SignedValue.Zero; return left < right; }", true, ExecutionMode.Compiled)]
    [InlineData("{ var left = SignedValue.Max; var right = SignedValue.Zero; return left > right; }", true, ExecutionMode.Interpreted)]
    [InlineData("{ var left = SignedValue.Max; var right = SignedValue.Zero; return left > right; }", true, ExecutionMode.Compiled)]
    public Task Enum_equality_and_signed_ordering_preserve_both_kernel_modes(string body, bool expected, ExecutionMode mode)
        => UiBooleanGeneratorFixture.AssertExecutes(body, expected, mode);
}
