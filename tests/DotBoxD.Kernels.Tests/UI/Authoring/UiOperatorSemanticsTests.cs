namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiOperatorSemanticsTests
{
    [Theory]
    [InlineData("{ var left = 1m; var right = 2m; return left < right; }")]
    [InlineData("{ var left = 1m; var right = 2m; return left <= right; }")]
    [InlineData("{ var left = 1m; var right = 2m; return left > right; }")]
    [InlineData("{ var left = 1m; var right = 2m; return left >= right; }")]
    [InlineData("{ var left = 1m; var right = 2m; return left + right > 0m; }")]
    [InlineData("{ var number = 1m; return -number < 0m; }")]
    [InlineData("=> new OperatorValue(value) + new OperatorValue(value) == 0;")]
    [InlineData("=> -new OperatorValue(value) == 0;")]
    [InlineData("{ var amount = 1m; amount += 2m; return true; }")]
    [InlineData("{ var amount = 1m; amount -= 2m; return true; }")]
    [InlineData("{ var amount = 1m; amount *= 2m; return true; }")]
    [InlineData("{ var amount = 1m; amount /= 2m; return true; }")]
    [InlineData("{ var amount = 1m; amount %= 2m; return true; }")]
    [InlineData("{ var amount = new OperatorValue(value); amount *= new OperatorValue(2); return true; }")]
    public void Unsupported_operator_semantics_fail_closed_during_generation(string body)
        => UiBooleanGeneratorFixture.AssertUnsupported(body);
}
