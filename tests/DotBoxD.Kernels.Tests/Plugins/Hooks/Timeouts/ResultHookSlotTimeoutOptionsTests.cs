using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class ResultHookSlotTimeoutOptionsTests
{
    private sealed record DamageCtx(int Damage);

    private readonly record struct TestResult(bool Success, string? Reason, int Value = 0) : IHookResult;

    private static ResultHookSlot<DamageCtx, HookContext> NewSlot(Action<ResultHookFault>? onFault = null)
        => new(new StubAdapter(), onFault);

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private static ValueTask<IHookResult?> Ok(int value) => new((IHookResult?)new TestResult(true, null, value));

    [Fact]
    public void FailClosedAfter_rejects_abstain_timeout_results()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ResultHookDispatchOptions<TestResult>.FailClosedAfter(
                TimeSpan.FromMilliseconds(100),
                new TestResult(false, "timeout", -1)));

        Assert.Equal("result", exception.ParamName);
    }

    [Fact]
    public async Task Hand_written_fail_closed_options_reject_abstain_timeout_results()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(1), remote: true);
        var options = new ResultHookDispatchOptions<TestResult>
        {
            RemoteTimeoutResult = new TestResult(false, "timeout", -1)
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () =>
            {
                var context = Context();
                await slot.FireAsync(new DamageCtx(10), context, context, options, CancellationToken.None);
            });

        Assert.Equal("RemoteTimeoutResult", exception.ParamName);
    }

    [Fact]
    public void Default_remote_timeout_is_finite()
    {
        Assert.NotEqual(
            Timeout.InfiniteTimeSpan,
            ResultHookDispatchOptions<TestResult>.Default.RemoteHandlerTimeout);
    }

    [Fact]
    public async Task Infinite_remote_timeout_is_explicit_opt_in()
    {
        var slot = NewSlot();
        slot.AddDirect(
            0,
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                return new TestResult(true, null, 5);
            },
            remote: true);
        var options = new ResultHookDispatchOptions<TestResult>
        {
            RemoteHandlerTimeout = Timeout.InfiniteTimeSpan
        };

        var context = Context();
        var result = await slot.FireAsync(new DamageCtx(10), context, context, options, CancellationToken.None);

        Assert.Equal(5, result!.Value.Value);
    }

    [Fact]
    public async Task Oversized_remote_timeout_is_rejected_before_dispatch()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(1), remote: true);
        var options = new ResultHookDispatchOptions<TestResult>
        {
            RemoteHandlerTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1)
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () =>
            {
                var context = Context();
                await slot.FireAsync(new DamageCtx(10), context, context, options, CancellationToken.None);
            });
    }

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}
