using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteLocalCrossKindRegistrationSurpriseTests
{
    [Fact]
    public async Task RegisterResult_replaces_a_push_handler_with_the_same_subscription_id()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var pushInvoked = false;
        registry.Register<int>(
            "shared-id",
            (_, _) =>
            {
                pushInvoked = true;
                return ValueTask.CompletedTask;
            },
            (Func<ReadOnlyMemory<byte>, int>)(static _ => 0));
        registry.RegisterResult<int, TestResult>(
            "shared-id",
            static (_, _) => new TestResult(true, "accepted"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("shared-id", ReadOnlyMemory<byte>.Empty, Context()));

        Assert.False(pushInvoked);
    }

    [Fact]
    public async Task Register_replaces_a_result_handler_with_the_same_subscription_id()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var resultInvoked = false;
        registry.RegisterResult<int, TestResult>(
            "shared-id",
            (_, _) =>
            {
                resultInvoked = true;
                return new TestResult(true, "accepted");
            });
        registry.Register<int>(
            "shared-id",
            static (_, _) => ValueTask.CompletedTask,
            (Func<ReadOnlyMemory<byte>, int>)(static _ => 0));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchResultAsync("shared-id", EncodeProjected(0), Context()));

        Assert.False(resultInvoked);
    }

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private static byte[] EncodeProjected<T>(T value)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcMarshaller.ToSandboxValue(value, typeof(T)));

    private readonly record struct TestResult(bool Success, string? Reason) : IHookResult;
}
