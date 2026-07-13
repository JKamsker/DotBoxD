using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteLocalHandlerRegistryCancellationTests
{
    [Fact]
    public async Task DispatchAsync_observes_pre_canceled_context_before_invoking_handler()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var invocations = 0;
        registry.Register<string>("sub-cancel", (_, _) =>
        {
            invocations++;
            return ValueTask.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);

        var exception = await Record.ExceptionAsync(
            async () => await registry.DispatchAsync("sub-cancel", EncodeProjected("payload"), context));

        Assert.Equal((typeof(OperationCanceledException), 0), (exception?.GetType(), invocations));
    }

    [Fact]
    public async Task DispatchResultAsync_observes_pre_canceled_context_before_invoking_handler()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var invocations = 0;
        registry.RegisterResult<DamageContext, DamageResult>("sub-result-cancel", (context, _) =>
        {
            invocations++;
            return new DamageResult(true, "ok", context.Damage);
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = new HookContext(new InMemoryPluginMessageSink(), cancellation.Token);

        var exception = await Record.ExceptionAsync(
            async () => await registry.DispatchResultAsync(
                "sub-result-cancel",
                EncodeProjected(new DamageContext(5)),
                context));

        Assert.Equal((typeof(OperationCanceledException), 0), (exception?.GetType(), invocations));
    }

    [Fact]
    public async Task DispatchResultAsync_observes_caller_cancellation_after_result_handler_returns()
    {
        var registry = new RemoteLocalHandlerRegistry();
        using var cancellation = new CancellationTokenSource();
        var observedToken = default(CancellationToken);
        registry.RegisterResult<DamageContext, DamageResult>(
            "sub-result-caller-cancel",
            (context, _, cancellationToken) =>
            {
                observedToken = cancellationToken;
                cancellation.Cancel();
                return new ValueTask<DamageResult>(new DamageResult(true, "ok", context.Damage));
            });

        byte[]? response = null;
        var exception = await Record.ExceptionAsync(
            async () => response = await registry.DispatchResultAsync(
                "sub-result-caller-cancel",
                EncodeProjected(new DamageContext(7)),
                new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None),
                cancellation.Token));

        Assert.Equal((typeof(OperationCanceledException), cancellation.Token, null), (exception?.GetType(), observedToken, response));
    }

    [Fact]
    public async Task DispatchResultAsync_observes_context_cancellation_after_result_handler_returns()
    {
        var registry = new RemoteLocalHandlerRegistry();
        using var contextCancellation = new CancellationTokenSource();
        registry.RegisterResult<DamageContext, DamageResult>(
            "sub-result-context-cancel",
            (context, _, _) =>
            {
                contextCancellation.Cancel();
                return new ValueTask<DamageResult>(new DamageResult(true, "ok", context.Damage));
            });

        byte[]? response = null;
        var exception = await Record.ExceptionAsync(
            async () => response = await registry.DispatchResultAsync(
                "sub-result-context-cancel",
                EncodeProjected(new DamageContext(9)),
                new HookContext(new InMemoryPluginMessageSink(), contextCancellation.Token)));

        Assert.Equal((typeof(OperationCanceledException), null), (exception?.GetType(), response));
    }

    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private sealed record DamageContext(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;
}
