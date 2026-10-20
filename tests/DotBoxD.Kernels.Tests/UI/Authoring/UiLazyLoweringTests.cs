namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiLazyLoweringTests
{
    [Theory]
    [InlineData("=> value == 0 || Positive(10 / value);")]
    [InlineData("=> value != 0 && Positive(10 / value);")]
    [InlineData("{ return value == 0 || Positive(10 / value); }")]
    [InlineData("{ return value != 0 && Positive(10 / value); }")]
    public void Short_circuit_operands_requiring_temporaries_fail_closed_at_compile_time(string body)
        => UiBooleanGeneratorFixture.AssertUnsupported(body);

    [Theory]
    [InlineData("{ if (value == 0) return true; return Positive(10 / value); }", true, ExecutionMode.Interpreted)]
    [InlineData("{ if (value == 0) return true; return Positive(10 / value); }", true, ExecutionMode.Compiled)]
    [InlineData("{ if (value == 0) return false; return Positive(10 / value); }", false, ExecutionMode.Interpreted)]
    [InlineData("{ if (value == 0) return false; return Positive(10 / value); }", false, ExecutionMode.Compiled)]
    [InlineData("=> value == 0 || 10 / value > 0;", true, ExecutionMode.Interpreted)]
    [InlineData("=> value == 0 || 10 / value > 0;", true, ExecutionMode.Compiled)]
    [InlineData("=> value != 0 && 10 / value > 0;", false, ExecutionMode.Interpreted)]
    [InlineData("=> value != 0 && 10 / value > 0;", false, ExecutionMode.Compiled)]
    public Task Explicit_guards_and_simple_lazy_operands_preserve_both_kernel_modes(string body, bool expected, ExecutionMode mode)
        => UiBooleanGeneratorFixture.AssertExecutes(body, expected, mode);
}
