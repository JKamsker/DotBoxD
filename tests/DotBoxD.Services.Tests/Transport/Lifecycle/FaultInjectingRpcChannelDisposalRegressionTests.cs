using DotBoxD.Services.Buffers;
using DotBoxD.Services.Testing;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class FaultInjectingRpcChannelDisposalRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DisposeAsync_ConcurrentCallsShareUnderlyingTerminal()
    {
        var failure = new InvalidOperationException("inner disposal failed");
        await using var inner = new GatedFaultingChannel(failure);
        var wrapper = new FaultInjectingRpcChannel(
            inner,
            static (_, _, _) => default);

        try
        {
            var firstDispose = wrapper.DisposeAsync().AsTask();
            await inner.FirstDisposeEntered.WaitAsync(Timeout);

            var secondDispose = wrapper.DisposeAsync().AsTask();

            Assert.Equal(1, inner.DisposeEntries);
            Assert.False(
                secondDispose.IsCompleted,
                "The second concurrent DisposeAsync completed before the inner teardown finished.");

            inner.Release();

            var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => firstDispose.WaitAsync(Timeout));
            var secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => secondDispose.WaitAsync(Timeout));

            Assert.Same(failure, firstFailure);
            Assert.Same(failure, secondFailure);
            Assert.Equal(1, inner.DisposeEntries);
        }
        finally
        {
            inner.Release();
        }
    }

    private sealed class GatedFaultingChannel(InvalidOperationException failure) : IRpcChannel
    {
        private readonly TaskCompletionSource _firstDisposeEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeEntries;

        public Task FirstDisposeEntered => _firstDisposeEntered.Task;

        public int DisposeEntries => Volatile.Read(ref _disposeEntries);

        public bool IsConnected => true;

        public string RemoteEndpoint => "gated://faulting";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromException<Payload>(new NotSupportedException());

        public void Release() => _release.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            var entry = Interlocked.Increment(ref _disposeEntries);
            if (entry != 1)
            {
                return;
            }

            _firstDisposeEntered.SetResult();
            await _release.Task.ConfigureAwait(false);
            throw failure;
        }
    }
}
