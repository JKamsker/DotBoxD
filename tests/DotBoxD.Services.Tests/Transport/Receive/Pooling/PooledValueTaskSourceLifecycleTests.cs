using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.Pooling;

public sealed class PooledValueTaskSourceLifecycleTests
{
    [Fact]
    public void ProducerFirst_ConsumerClaimsReturn()
    {
        var lifecycle = new PooledValueTaskSourceLifecycle();
        lifecycle.Initialize();

        Assert.False(lifecycle.FinishProducer());
        Assert.True(lifecycle.TryBeginReading());
        Assert.True(lifecycle.FinishReading());
        Assert.False(lifecycle.TryBeginReading());
    }

    [Fact]
    public void ConsumerFirst_ProducerClaimsReturn()
    {
        var lifecycle = new PooledValueTaskSourceLifecycle();
        lifecycle.Initialize();

        Assert.True(lifecycle.TryBeginReading());
        Assert.False(lifecycle.FinishReading());
        Assert.True(lifecycle.FinishProducer());
        Assert.False(lifecycle.TryBeginReading());
    }

    [Fact]
    public void RollBackReading_RestoresConsumerLease()
    {
        var lifecycle = new PooledValueTaskSourceLifecycle();
        lifecycle.Initialize();

        Assert.True(lifecycle.TryBeginReading());
        lifecycle.RollBackReading();
        Assert.True(lifecycle.TryBeginReading());
        Assert.False(lifecycle.FinishReading());
        Assert.True(lifecycle.FinishProducer());
    }

    [Fact]
    public async Task ConcurrentProducerAndConsumer_ExactlyOneClaimsReturn()
    {
        const int iterations = 256;

        for (var i = 0; i < iterations; i++)
        {
            var state = new LifecycleState();
            state.Lifecycle.Initialize();
            Assert.True(state.Lifecycle.TryBeginReading());
            using var start = new Barrier(2);

            var producer = Task.Run(() =>
            {
                start.SignalAndWait();
                state.ProducerClaimed = state.Lifecycle.FinishProducer();
            });
            var consumer = Task.Run(() =>
            {
                start.SignalAndWait();
                state.ConsumerClaimed = state.Lifecycle.FinishReading();
            });

            await Task.WhenAll(producer, consumer);
            Assert.NotEqual(state.ProducerClaimed, state.ConsumerClaimed);
        }
    }

    private sealed class LifecycleState
    {
        public PooledValueTaskSourceLifecycle Lifecycle;
        public bool ProducerClaimed;
        public bool ConsumerClaimed;
    }
}
