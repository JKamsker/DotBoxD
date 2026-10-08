namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiReferenceIdentityTests
{
    [Theory]
    [InlineData("{ var left = new List<int>(); var right = new List<int>(); return left == right; }")]
    [InlineData("{ var left = new List<int>(); var right = new List<int>(); return left != right; }")]
    [InlineData("{ var left = new List<int>(); var alias = left; return left == alias; }")]
    [InlineData("{ var left = new Dictionary<int, int>(); var right = new Dictionary<int, int>(); return left == right; }")]
    [InlineData("=> new ValueRecord(value) == new ValueRecord(value);")]
    [InlineData("=> new ValueStruct(value) == new ValueStruct(value);")]
    public void Reference_and_user_defined_equality_fail_closed_at_compile_time(string body)
        => UiBooleanGeneratorFixture.AssertUnsupported(body);

    [Theory]
    [InlineData("=> value == 0;", true, ExecutionMode.Interpreted)]
    [InlineData("=> value == 0;", true, ExecutionMode.Compiled)]
    [InlineData("=> value != 0;", false, ExecutionMode.Interpreted)]
    [InlineData("=> value != 0;", false, ExecutionMode.Compiled)]
    [InlineData("{ var left = \"ab\"; var right = \"a\" + \"b\"; return left == right; }", true, ExecutionMode.Interpreted)]
    [InlineData("{ var left = \"ab\"; var right = \"a\" + \"b\"; return left == right; }", true, ExecutionMode.Compiled)]
    [InlineData("{ var left = true; var right = false; return left != right; }", true, ExecutionMode.Interpreted)]
    [InlineData("{ var left = true; var right = false; return left != right; }", true, ExecutionMode.Compiled)]
    public Task Scalar_equality_preserves_execution_in_both_kernel_modes(string body, bool expected, ExecutionMode mode)
        => UiBooleanGeneratorFixture.AssertExecutes(body, expected, mode);
}
