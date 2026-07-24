using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.LookaheadLifecycle;

public sealed class ReceiveBufferHandoffTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Fact]
    public void DisposePublication_AfterReceiveEntry_KeepsBufferUntilReceiveExit()
    {
        var state = new HandoffState(hasCarry: true);
        try
        {
            Assert.True(state.HasBuffer);

            Assert.True(state.PublishDisposed());

            Assert.True(state.IsDisposed);
            Assert.True(state.HasBuffer);

            state.ExitReceive();

            Assert.False(state.HasBuffer);
            Assert.False(state.IsReceiveActive);
        }
        finally
        {
            state.ReleaseBuffer();
        }
    }

    [Fact]
    public void DisposedActiveLifecycle_RejectsSuccessorUntilOwnerFinishesCleanup()
    {
        var state = new HandoffState(hasCarry: true);
        try
        {
            Assert.True(state.PublishDisposed());

            Assert.Throws<ObjectDisposedException>(state.EnterReceive);
            Assert.True(state.IsReceiveActive);
            Assert.True(state.HasBuffer);

            state.ExitReceive();

            Assert.False(state.IsReceiveActive);
            Assert.False(state.HasBuffer);
            Assert.Throws<ObjectDisposedException>(state.EnterReceive);
        }
        finally
        {
            state.ReleaseBuffer();
        }
    }

    [Fact]
    public void ReceiveExit_ReleasesDrainedBufferBeforeDisposePublication()
    {
        var state = new HandoffState();
        try
        {
            state.ExitReceive();

            Assert.False(state.HasBuffer);
            Assert.False(state.IsReceiveActive);
            Assert.True(state.PublishDisposed());

            Assert.True(state.IsDisposed);
            Assert.False(state.HasBuffer);
            Assert.False(state.PublishDisposed());

        }
        finally
        {
            state.ReleaseBuffer();
        }
    }

    [Fact]
    public async Task DisposePublication_RacingReceiveExit_DoesNotMissBufferRelease()
    {
        const int iterations = 128;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var state = new HandoffState(hasCarry: true);
            try
            {
                var start = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var publish = Task.Run(async () =>
                {
                    await start.Task;
                    return state.PublishDisposed();
                });
                var exit = Task.Run(async () =>
                {
                    await start.Task;
                    state.ExitReceive();
                });

                start.SetResult();

                Assert.True(await publish.WaitAsync(Guard));
                await exit.WaitAsync(Guard);

                Assert.True(state.IsDisposed);
                Assert.False(state.IsReceiveActive);
                Assert.False(state.HasBuffer);
            }
            finally
            {
                state.ReleaseBuffer();
            }
        }
    }

    [Fact]
    public async Task DisposePublication_RacingFirstReceiveCreation_DoesNotMissBufferRelease()
    {
        const int iterations = 256;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var state = new LazyHandoffState();
            try
            {
                var start = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var receive = Task.Run(async () =>
                {
                    await start.Task;
                    state.EnterAndCreateUnlessDisposed();
                });
                var dispose = Task.Run(async () =>
                {
                    await start.Task;
                    state.PublishDisposed();
                });

                start.SetResult();
                await Task.WhenAll(receive, dispose).WaitAsync(Guard);

                Assert.True(state.IsDisposed);
                Assert.False(state.IsReceiveActive);
                Assert.False(state.HasBuffer);
            }
            finally
            {
                state.ReleaseBuffer();
            }
        }
    }

    [Fact]
    public void DisposePublication_AfterLiveCheckBeforeFirstRent_TransfersCleanupToReceiver()
    {
        var state = new LazyHandoffState();
        try
        {
            state.EnterReceive();
            Assert.False(state.IsDisposed);

            state.PublishDisposed();
            state.CreateBufferAfterLiveCheck();

            Assert.True(state.IsReceiveActive);
            Assert.True(state.HasBuffer);
            state.ExitReceive();
            Assert.False(state.IsReceiveActive);
            Assert.False(state.HasBuffer);
            Assert.Throws<ObjectDisposedException>(state.EnterReceive);
        }
        finally
        {
            state.ReleaseBuffer();
        }
    }

    private sealed class HandoffState
    {
        private StreamFrameReceiveBuffer _buffer;
        private int _activeReceive;
        private int _disposed;

        public HandoffState(bool hasCarry = false)
        {
            _ = _buffer.PrepareRead();
            if (hasCarry)
            {
                _ = _buffer.CommitRead(count: 1);
            }

            ReceiveConcurrencyGuard.Enter(ref _activeReceive, nameof(HandoffState));
        }

        public bool HasBuffer => _buffer.HasBuffer;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public bool IsReceiveActive =>
            ReceiveConcurrencyGuard.IsActive(Volatile.Read(ref _activeReceive));

        public void EnterReceive() =>
            ReceiveConcurrencyGuard.Enter(ref _activeReceive, nameof(HandoffState));

        public bool PublishDisposed() =>
            ReceiveConcurrencyGuard.TryPublishDisposedAndReleaseBufferIfIdle(
                ref _disposed,
                ref _activeReceive,
                hasPooledBuffer: true,
                ref _buffer);

        public void ExitReceive() =>
            ReceiveConcurrencyGuard.Exit(ref _activeReceive, ref _buffer);

        public void ReleaseBuffer() => _buffer.ReturnPooledBuffer();
    }

    private sealed class LazyHandoffState
    {
        private StreamFrameReceiveBuffer _buffer;
        private int _activeReceive;
        private int _disposed;

        public bool HasBuffer => _buffer.HasBuffer;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public bool IsReceiveActive =>
            ReceiveConcurrencyGuard.IsActive(Volatile.Read(ref _activeReceive));

        public void EnterReceive() =>
            ReceiveConcurrencyGuard.Enter(ref _activeReceive, nameof(LazyHandoffState));

        public void CreateBufferAfterLiveCheck()
        {
            _buffer.BeginFrame();
            _ = _buffer.PrepareRead();
            _ = _buffer.CommitRead(count: 1);
        }

        public void ExitReceive() =>
            ReceiveConcurrencyGuard.Exit(ref _activeReceive, ref _buffer);

        public void EnterAndCreateUnlessDisposed()
        {
            try
            {
                EnterReceive();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    CreateBufferAfterLiveCheck();
                }
            }
            finally
            {
                ExitReceive();
            }
        }

        public void PublishDisposed() =>
            ReceiveConcurrencyGuard.TryPublishDisposedAndReleaseBufferIfIdle(
                ref _disposed,
                ref _activeReceive,
                hasPooledBuffer: true,
                ref _buffer);

        public void ReleaseBuffer() => _buffer.ReturnPooledBuffer();
    }
}
