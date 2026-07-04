using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class ResultHookDispatchOptionsTests
{
    private readonly record struct TestResult(bool Success, string? Reason, int Value = 0) : IHookResult;

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2147483648d)]
    public void FailClosedAfter_rejects_invalid_timeouts_at_factory_boundary(double milliseconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ResultHookDispatchOptions<TestResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(milliseconds),
                new TestResult(true, "timeout")));

        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public void FailClosedAfter_accepts_positive_timeout()
    {
        var options = ResultHookDispatchOptions<TestResult>.FailClosedAfter(
            TimeSpan.FromMilliseconds(1),
            new TestResult(true, "timeout", -1));

        Assert.Equal(TimeSpan.FromMilliseconds(1), options.RemoteHandlerTimeout);
        Assert.Equal(new TestResult(true, "timeout", -1), options.RemoteTimeoutResult);
    }
}
