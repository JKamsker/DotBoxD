using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.RemoteLocal;

public sealed class RemoteLocalResultEncodingSurpriseTests
{
    [Fact]
    public async Task DispatchResultAsync_wraps_throwing_result_member_getters_with_encoding_context()
    {
        var registry = new RemoteLocalHandlerRegistry();
        registry.RegisterResult<DamageContext, ThrowingDamageResult>(
            "throwing-result",
            (context, _) => new ThrowingDamageResult(context.Damage));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchResultAsync(
                "throwing-result",
                EncodeProjected(new DamageContext(21)),
                new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None)));

        Assert.Contains("hook result", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(ThrowingDamageResult.Damage), exception.Message, StringComparison.Ordinal);

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("result getter failed", inner.Message);
    }

    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private sealed record DamageContext(int Damage);

    private readonly struct ThrowingDamageResult : IHookResult
    {
        private readonly int _damage;

        public ThrowingDamageResult(int damage)
        {
            _damage = damage;
        }

        public bool Success => true;

        public string? Reason => "ok";

        public int Damage
        {
            get
            {
                _ = _damage;
                throw new InvalidOperationException("result getter failed");
            }
        }
    }
}
