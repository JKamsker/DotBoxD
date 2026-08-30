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

    [Fact]
    public async Task DisposeAsync_ReentrantInnerDisposalSharesOneTerminal()
    {
        var failure = new InvalidOperationException("inner disposal failed");
        var inner = new ReentrantFaultingChannel(failure);
        var wrapper = new FaultInjectingRpcChannel(
            inner,
            static (_, _, _) => default);
        inner.Attach(wrapper);

        var outerDispose = wrapper.DisposeAsync().AsTask();
        var nestedDispose = inner.NestedDispose
            ?? throw new InvalidOperationException("The inner channel did not reenter disposal.");

        Assert.Equal(1, inner.DisposeEntries);
        Assert.Same(outerDispose, nestedDispose);

        var outerFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => outerDispose.WaitAsync(Timeout));
        var nestedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nestedDispose.WaitAsync(Timeout));

        Assert.Same(failure, outerFailure);
        Assert.Same(failure, nestedFailure);
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

    private sealed class ReentrantFaultingChannel(InvalidOperationException failure) : IRpcChannel
    {
        private FaultInjectingRpcChannel? _wrapper;
        private int _disposeEntries;

        public Task? NestedDispose { get; private set; }

        public int DisposeEntries => Volatile.Read(ref _disposeEntries);

        public bool IsConnected => true;

        public string RemoteEndpoint => "reentrant://faulting";

        public void Attach(FaultInjectingRpcChannel wrapper) => _wrapper = wrapper;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException());

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromException<Payload>(new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeEntries) == 1)
            {
                NestedDispose = _wrapper!.DisposeAsync().AsTask();
            }

            return ValueTask.FromException(failure);
        }
    }
}
