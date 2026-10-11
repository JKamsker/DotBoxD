namespace DotBoxD.UI.Blazor.Tests.Rendering;

public sealed class BlazorRendererDisposalTerminalSurpriseTests
{
    [Fact]
    public async Task Concurrent_disposal_waits_for_the_viewer_teardown_shared_by_the_first_call()
    {
        var renderer = new BlazorUiRenderer();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCalls = 0;
        using var subscription = renderer.Subscribe(async () =>
        {
            Interlocked.Increment(ref callbackCalls);
            callbackEntered.TrySetResult();
            await releaseCallback.Task;
        });

        var firstDisposal = renderer.DisposeAsync().AsTask();
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var concurrentDisposal = renderer.DisposeAsync().AsTask();

        try
        { Assert.False(concurrentDisposal.IsCompleted); }
        finally
        {
            releaseCallback.TrySetResult();
            await Task.WhenAll(firstDisposal, concurrentDisposal).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, callbackCalls);
    }
}
