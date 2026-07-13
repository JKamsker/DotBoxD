using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteLocalProjectedDecoderSurpriseTests
{
    [Fact]
    public async Task DispatchAsync_wraps_throwing_projected_payload_decoders_with_subscription_context()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var handlerInvocations = 0;
        var decoderFailure = new InvalidOperationException("decoder failed");
        registry.Register(
            "sub-projected",
            (ProjectedPayload _, HookContext _) =>
            {
                handlerInvocations++;
                return ValueTask.CompletedTask;
            },
            (Func<ReadOnlyMemory<byte>, ProjectedPayload>)(_ => throw decoderFailure));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DispatchAsync("sub-projected", new byte[] { 1, 2, 3 }, Context()));

        Assert.Equal(0, handlerInvocations);
        Assert.Contains("remote-local", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("projected", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payload", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sub-projected", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ProjectedPayload), exception.Message, StringComparison.Ordinal);
        Assert.Same(decoderFailure, exception.InnerException);
    }

    [Fact]
    public async Task DispatchAsync_preserves_projected_payload_decoder_cancellation()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var handlerInvocations = 0;
        using var cancellation = new CancellationTokenSource();
        var decoderCancellation = new OperationCanceledException(cancellation.Token);
        registry.Register(
            "sub-cancel",
            (ProjectedPayload _, HookContext _) =>
            {
                handlerInvocations++;
                return ValueTask.CompletedTask;
            },
            (Func<ReadOnlyMemory<byte>, ProjectedPayload>)(_ => throw decoderCancellation));

        var exception = await Record.ExceptionAsync(
            async () => await registry.DispatchAsync("sub-cancel", new byte[] { 1 }, Context()));

        Assert.Equal(0, handlerInvocations);
        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Same(decoderCancellation, canceled);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    private static HookContext Context() => new(new InMemoryPluginMessageSink(), CancellationToken.None);

    private sealed record ProjectedPayload(string Id);
}
