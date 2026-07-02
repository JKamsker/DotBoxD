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

    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private sealed record DamageContext(int Damage);

    private readonly record struct DamageResult(bool Success, string? Reason, int Damage) : IHookResult;
}
