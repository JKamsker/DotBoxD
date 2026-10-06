using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.Cancellation;

public sealed class ResultHookSlotCancellationPrecedenceSurpriseTests
{
    private sealed record DamageEvent;

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;

    [Fact]
    public async Task FireAsync_prioritizes_callback_triggered_caller_cancellation_over_ordinary_handler_fault()
    {
        var faults = new List<ResultHookFault>();
        var slot = NewSlot(faults.Add);
        using var cancellation = new CancellationTokenSource();
        var fallbackInvoked = false;
        var context = Context(cancellation.Token);

        slot.AddDirect(
            priority: 100,
            (_, _, _) =>
            {
                cancellation.Cancel();
                throw new InvalidOperationException("handler fault after caller cancellation");
            });
        slot.AddDirect(
            priority: 0,
            (_, _, _) =>
            {
                fallbackInvoked = true;
                return ValueTask.FromResult<IHookResult?>(new DamageResult(true, null, 42));
            });

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => slot.FireAsync<DamageResult>(new DamageEvent(), context, context, cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(faults);
        Assert.False(fallbackInvoked);
    }

    [Fact]
    public async Task FireAsync_isolates_ordinary_handler_fault_while_caller_token_remains_active()
    {
        var faults = new List<ResultHookFault>();
        var slot = NewSlot(faults.Add);
        using var cancellation = new CancellationTokenSource();
        var fallbackInvoked = false;
        var handlerFailure = new InvalidOperationException("ordinary handler fault");
        var context = Context(cancellation.Token);

        slot.AddDirect(100, (_, _, _) => throw handlerFailure);
        slot.AddDirect(
            0,
            (_, _, _) =>
            {
                fallbackInvoked = true;
                return ValueTask.FromResult<IHookResult?>(new DamageResult(true, null, 42));
            });

        var result = await slot.FireAsync<DamageResult>(
            new DamageEvent(),
            context,
            context,
            cancellation.Token);

        Assert.Equal(42, result!.Value.Damage);
        Assert.True(fallbackInvoked);
        Assert.Same(handlerFailure, Assert.Single(faults).Exception);
    }

    private static ResultHookSlot<DamageEvent, HookContext> NewSlot(Action<ResultHookFault> onFault)
        => new(new StubAdapter(), onFault);

    private static HookContext Context(CancellationToken cancellationToken)
        => new(new InMemoryPluginMessageSink(), cancellationToken);

    private sealed class StubAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "test.result-cancellation-precedence";

        public IReadOnlyList<Parameter> Parameters => [];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e) => [];
    }
}
