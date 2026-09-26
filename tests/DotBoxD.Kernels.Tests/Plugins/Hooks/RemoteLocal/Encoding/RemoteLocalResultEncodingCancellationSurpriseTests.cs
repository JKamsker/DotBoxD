using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.RemoteLocal;

public sealed class RemoteLocalResultEncodingCancellationSurpriseTests
{
    [Fact]
    public async Task DispatchResultAsync_observes_context_cancellation_during_result_member_encoding()
    {
        var registry = new RemoteLocalHandlerRegistry();
        using var contextCancellation = new CancellationTokenSource();
        registry.RegisterResult<DamageContext, CancelingDamageResult>(
            "result-encoding-context-cancel",
            (context, _) => new CancelingDamageResult(contextCancellation, context.Damage));

        byte[]? response = null;
        var exception = await Record.ExceptionAsync(
            async () => response = await registry.DispatchResultAsync(
                "result-encoding-context-cancel",
                EncodeProjected(new DamageContext(21)),
                new HookContext(new InMemoryPluginMessageSink(), contextCancellation.Token)));

        Assert.Equal((typeof(OperationCanceledException), null), (exception?.GetType(), response));
    }

    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private sealed record DamageContext(int Damage);

    private readonly struct CancelingDamageResult(CancellationTokenSource cancellation, int damage) : IHookResult
    {
        public bool Success
        {
            get
            {
                cancellation.Cancel();
                return true;
            }
        }

        public string? Reason => "ok";

        public int Damage => damage;
    }
}
