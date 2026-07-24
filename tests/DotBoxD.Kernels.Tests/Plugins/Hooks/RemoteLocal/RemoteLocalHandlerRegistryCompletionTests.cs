using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class RemoteLocalHandlerRegistryCompletionTests
{
    [Fact]
    public async Task DispatchAsync_captures_synchronous_decoder_failures()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var decoderError = new FormatException("invalid projected payload");
        registry.Register<int>(
            "sub-decoder",
            static (_, _) => ValueTask.CompletedTask,
            (Func<ReadOnlyMemory<byte>, int>)(_ => throw decoderError));

        var pending = default(ValueTask);
        var synchronousError = Record.Exception(
            () => pending = registry.DispatchAsync("sub-decoder", new byte[] { 1 }, Context()));

        Assert.Null(synchronousError);
        Assert.True(pending.IsCompleted);
        var dispatchError = await Assert.ThrowsAsync<InvalidOperationException>(() => pending.AsTask());
        Assert.Same(decoderError, dispatchError.InnerException);
    }

    [Fact]
    public async Task DispatchAsync_captures_synchronous_missing_handler_failures()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var pending = default(ValueTask);

        var synchronousError = Record.Exception(
            () => pending = registry.DispatchAsync("missing", ReadOnlyMemory<byte>.Empty, Context()));

        Assert.Null(synchronousError);
        Assert.True(pending.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.AsTask());
    }

    [Fact]
    public async Task DispatchAsync_preserves_synchronously_faulted_handler_result()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var handlerError = new InvalidOperationException("handler failed");
        registry.Register<int>(
            "sub-fault",
            (_, _) => ValueTask.FromException(handlerError),
            (Func<ReadOnlyMemory<byte>, int>)(static _ => 42));

        var pending = registry.DispatchAsync("sub-fault", ReadOnlyMemory<byte>.Empty, Context());

        Assert.True(pending.IsCompleted);
        var observed = await Record.ExceptionAsync(async () => await pending);
        Assert.Same(handlerError, observed);
    }

    [Fact]
    public async Task DispatchAsync_preserves_canceled_status_and_caller_token()
    {
        var registry = new RemoteLocalHandlerRegistry();
        registry.Register<int>(
            "sub-cancel",
            static (_, _) => ValueTask.CompletedTask,
            (Func<ReadOnlyMemory<byte>, int>)(static _ => 42));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var pending = default(ValueTask);

        var synchronousError = Record.Exception(
            () => pending = registry.DispatchAsync(
                "sub-cancel",
                ReadOnlyMemory<byte>.Empty,
                Context(),
                cancellation.Token));

        Assert.Null(synchronousError);
        var task = pending.AsTask();
        Assert.True(task.IsCanceled);
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    [Fact]
    public async Task DispatchAsync_preserves_canceled_handler_result_and_token()
    {
        var registry = new RemoteLocalHandlerRegistry();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        registry.Register<int>(
            "sub-handler-cancel",
            (_, _) => ValueTask.FromCanceled(cancellation.Token),
            (Func<ReadOnlyMemory<byte>, int>)(static _ => 42));

        var pending = registry.DispatchAsync("sub-handler-cancel", ReadOnlyMemory<byte>.Empty, Context());

        var task = pending.AsTask();
        Assert.True(task.IsCanceled);
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
    }

    [Fact]
    public async Task DispatchAsync_returns_pending_handler_completion()
    {
        var registry = new RemoteLocalHandlerRegistry();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        registry.Register<int>(
            "sub-pending",
            (_, _) =>
            {
                invocations++;
                return new ValueTask(completion.Task);
            },
            (Func<ReadOnlyMemory<byte>, int>)(static _ => 42));

        var pending = registry.DispatchAsync("sub-pending", ReadOnlyMemory<byte>.Empty, Context());

        Assert.False(pending.IsCompleted);
        Assert.Equal(1, invocations);
        completion.SetResult(null);
        await pending;
        Assert.Equal(1, invocations);
    }

    private static HookContext Context() =>
        new(new InMemoryPluginMessageSink(), CancellationToken.None);
}
