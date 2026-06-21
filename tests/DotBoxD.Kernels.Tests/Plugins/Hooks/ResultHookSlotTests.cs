using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// Result-hook dispatch semantics: descending-priority ordering, abstain/fallthrough, install-order tie-breaks,
/// first-success-wins, fault isolation, and cancellation — exercised against <see cref="ResultHookSlot{TEvent}"/>
/// directly with pure delegate handlers (no sandbox kernel needed).
/// </summary>
public sealed class ResultHookSlotTests
{
    private sealed record DamageCtx(int Damage);

    private readonly record struct TestResult(bool Success, string? Reason, int Value = 0) : IHookResult;

    private static ResultHookSlot<DamageCtx> NewSlot() => new(new StubAdapter());

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private static ValueTask<IHookResult?> Ok(int value) => new((IHookResult?)new TestResult(true, null, value));

    private static ValueTask<IHookResult?> Abstain() => new((IHookResult?)new TestResult(false, "abstain"));

    private static ValueTask<IHookResult?> FilterMiss() => new((IHookResult?)null);

    [Fact]
    public async Task No_handlers_returns_null()
    {
        var slot = NewSlot();

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Single_successful_handler_wins()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(42));

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Equal(42, result!.Value.Value);
    }

    [Fact]
    public async Task Filter_miss_does_not_produce_a_result()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => FilterMiss());

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Abstain_falls_through_to_the_next_successful_handler()
    {
        var slot = NewSlot();
        slot.AddDirect(100, (_, _, _) => Abstain());
        slot.AddDirect(0, (_, _, _) => Ok(7));

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Equal(7, result!.Value.Value);
    }

    [Fact]
    public async Task Higher_priority_result_wins()
    {
        var slot = NewSlot();
        slot.AddDirect(0, (_, _, _) => Ok(1));
        slot.AddDirect(100, (_, _, _) => Ok(2));

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Equal(2, result!.Value.Value);
    }

    [Fact]
    public async Task Same_priority_preserves_install_order()
    {
        var slot = NewSlot();
        slot.AddDirect(5, (_, _, _) => Ok(1));
        slot.AddDirect(5, (_, _, _) => Ok(2));

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Equal(1, result!.Value.Value);
    }

    [Fact]
    public async Task Faulty_handler_is_isolated_and_dispatch_continues()
    {
        var slot = NewSlot();
        slot.AddDirect(100, (_, _, _) => throw new InvalidOperationException("boom"));
        slot.AddDirect(0, (_, _, _) => Ok(9));

        var result = await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), CancellationToken.None);

        Assert.Equal(9, result!.Value.Value);
    }

    [Fact]
    public async Task Cancellation_stops_dispatch()
    {
        var slot = NewSlot();
        var invoked = false;
        slot.AddDirect(0, (_, _, _) =>
        {
            invoked = true;
            return Ok(1);
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await slot.FireAsync<TestResult>(new DamageCtx(10), Context(), cts.Token));
        Assert.False(invoked);
    }

    private sealed class StubAdapter : IPluginEventAdapter<DamageCtx>
    {
        public string EventName => "test.damage";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageCtx e) => [];
    }
}
