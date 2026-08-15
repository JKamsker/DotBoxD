using System.Collections.Concurrent;

namespace DotBoxD.Kernels.Tests.Plugins.Messaging;

public sealed class PluginMessageSinkAsyncDispatchSurpriseTests
{
    [Fact]
    public async Task Default_send_completes_for_async_only_sink_without_pumping_captured_context()
    {
        var context = new ControllableSynchronizationContext();
        var sink = new YieldingPluginMessageSink();
        var sendCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);

            try
            {
                ((IPluginMessageSink)sink).Send("target-1", "message");
                sendCompleted.SetResult();
            }
            catch (Exception exception)
            {
                sendCompleted.SetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        });

        sendThread.Start();
        await context.ContinuationPosted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var completedWithoutPumping = sendCompleted.Task.IsCompleted;

        context.PumpOne();
        await sendCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sendThread.Join(TimeSpan.FromSeconds(5));

        Assert.True(completedWithoutPumping, "The default Send implementation must not block an async sink's captured continuation.");
        Assert.Equal(1, sink.SendCount);
    }

    private sealed class YieldingPluginMessageSink : IPluginMessageSink
    {
        public int SendCount { get; private set; }

        public async ValueTask SendAsync(string targetId, string message, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            SendCount++;
        }
    }

    private sealed class ControllableSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public TaskCompletionSource ContinuationPosted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue((callback, state));
            ContinuationPosted.TrySetResult();
        }

        public void PumpOne()
        {
            Assert.True(_callbacks.TryDequeue(out var callback), "Expected the async continuation to be posted.");
            callback.Callback(callback.State);
        }
    }
}
