using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteLocalProjectedDecoderCallerCancellationSurpriseTests
{
    [Fact]
    public async Task DispatchAsync_does_not_invoke_handler_after_raw_decoder_cancels_caller_token()
    {
        var registry = new RemoteLocalHandlerRegistry();
        using var cancellation = new CancellationTokenSource();
        var handlerInvocations = 0;
        registry.Register(
            "decoder-caller-cancel",
            (string _, HookContext _) =>
            {
                handlerInvocations++;
                return ValueTask.CompletedTask;
            },
            (ReadOnlyMemory<byte> _) =>
            {
                cancellation.Cancel();
                return "payload";
            });

        var exception = await Record.ExceptionAsync(
            async () => await registry.DispatchAsync(
                "decoder-caller-cancel",
                new byte[] { 1 },
                new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None),
                cancellation.Token));

        Assert.Equal(0, handlerInvocations);
        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }
}
