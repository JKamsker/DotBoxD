using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Serialization;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class TaskBackedDirectCompletionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Duplicate_direct_owner_publication_fails_closed()
    {
        await using var firstOwner = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        await using var secondOwner = new ValueTaskTimeoutTestHarness(Timeout.InfiniteTimeSpan);
        var pending = new PendingUnaryResponse<int>(messageId: 1);

        pending.EnableDirectCompletion(firstOwner.Invoker);
        var error = Assert.Throws<InvalidOperationException>(
            () => pending.EnableDirectCompletion(secondOwner.Invoker));

        Assert.Equal("Direct pending owner was already published.", error.Message);
    }

    [Fact]
    public async Task Reentrant_success_before_owner_publication_releases_slot()
    {
        await using var harness = CreateReentrantHarness(ReentrantResponseKind.Success);

        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 1));
        Assert.IsType<PendingUnaryResponse<int>>(harness.LastSentPendingResponse);

        harness.ReentrantResponse = ReentrantResponseKind.None;
        var held = InvokeTask(harness, request: 2);
        var heldMessageId = harness.LastRequestMessageId;
        await Assert.ThrowsAsync<ServiceException>(
            () => InvokeTask(harness, request: 3).WaitAsync(TestTimeout));

        harness.CompleteGeneric(heldMessageId);
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await held);
    }

    [Fact]
    public async Task Reentrant_remote_error_before_owner_publication_releases_slot()
    {
        await using var harness = CreateReentrantHarness(ReentrantResponseKind.Error);

        await Assert.ThrowsAsync<RemoteServiceException>(() => InvokeTask(harness, request: 1));

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    [Fact]
    public async Task Reentrant_connection_error_before_owner_publication_releases_slot()
    {
        await using var harness = CreateReentrantHarness(ReentrantResponseKind.ConnectionFailure);

        var error = await Assert.ThrowsAsync<ServiceConnectionException>(
            () => InvokeTask(harness, request: 1));
        Assert.Equal(ValueTaskTimeoutTestHarness.ConnectionFailureMessage, error.Message);

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    [Fact]
    public async Task Direct_timeout_releases_slot_and_sends_one_cancel()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            requestTimeout: TimeSpan.FromMilliseconds(80));

        var call = InvokeTask(harness, request: 1);
        var pending = Assert.IsType<TimeoutOnlyPendingUnaryResponse<int>>(
            harness.LastSentPendingResponse);

        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => call.WaitAsync(TestTimeout));
        Assert.Equal("Request to FiniteTimeout.Unary timed out.", error.Message);
        Assert.Equal(PendingCancellationKind.Timeout, pending.CancellationKind);
        Assert.Equal(harness.LastRequestMessageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    [Fact]
    public async Task Disabled_low_allocation_ValueTask_with_finite_timeout_uses_timeout_only_state()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            TimeSpan.FromSeconds(30),
            enableLowAllocation: false);

        var call = InvokeOptionOffValueTask(harness, request: 1);
        var pending = Assert.IsType<TimeoutOnlyPendingUnaryResponse<int>>(
            harness.LastSentPendingResponse);
        harness.CompleteGeneric(harness.LastRequestMessageId);

        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await call);
        Assert.Equal(PendingCancellationKind.None, pending.CancellationKind);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Finite_timeout_with_live_caller_uses_combined_cancellable_state()
    {
        await using var harness = new ValueTaskTimeoutTestHarness(
            TimeSpan.FromSeconds(30),
            enableLowAllocation: false);
        using var cancellation = new CancellationTokenSource();

        var call = InvokeOptionOffValueTask(harness, request: 1, cancellation.Token);
        var pending = Assert.IsType<PendingUnaryResponseWithTimeout<int>>(
            harness.LastSentPendingResponse);
        var messageId = harness.LastRequestMessageId;
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => call.AsTask().WaitAsync(TestTimeout));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(PendingCancellationKind.Caller, pending.CancellationKind);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);
    }

    [Fact]
    public async Task Timeout_only_response_deserializer_cancellation_remains_a_response_failure()
    {
        var expected = new OperationCanceledException("Synthetic deserializer cancellation.");
        await using var harness = new ValueTaskTimeoutTestHarness(
            TimeSpan.FromSeconds(30),
            serializer: new OperationCanceledResponseSerializer(expected),
            enableLowAllocation: false);

        var call = InvokeOptionOffValueTask(harness, request: 1);
        var pending = Assert.IsType<TimeoutOnlyPendingUnaryResponse<int>>(
            harness.LastSentPendingResponse);
        harness.CompleteGeneric(harness.LastRequestMessageId);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.AsTask());
        Assert.Same(expected, error);
        Assert.True(call.IsFaulted);
        Assert.Equal(PendingCancellationKind.None, pending.CancellationKind);
        Assert.Equal(0, harness.CancelCount);
    }

    [Fact]
    public async Task Pending_send_starts_timeout_after_send_and_sends_one_cancel()
    {
        var requestTimeout = TimeSpan.FromMilliseconds(80);
        await using var harness = new ValueTaskTimeoutTestHarness(
            requestTimeout,
            blockRequestSend: true);

        var call = InvokeTask(harness, request: 1);
        var messageId = harness.LastRequestMessageId;
        var pending = Assert.IsType<TimeoutOnlyPendingUnaryResponse<int>>(
            harness.LastSentPendingResponse);
        var premature = await Task.WhenAny(call, Task.Delay(requestTimeout * 3));
        Assert.NotSame(call, premature);

        harness.ReleaseRequestSend();
        await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.WaitAsync(TestTimeout));
        Assert.Equal(PendingCancellationKind.Timeout, pending.CancellationKind);
        Assert.Equal(messageId, await harness.WaitForCancelAsync(TestTimeout));
        Assert.Equal(1, harness.CancelCount);

        harness.ReentrantResponse = ReentrantResponseKind.Success;
        Assert.Equal(ValueTaskTimeoutTestHarness.ResponseValue, await InvokeTask(harness, request: 2));
    }

    private static ValueTaskTimeoutTestHarness CreateReentrantHarness(ReentrantResponseKind response) =>
        new(Timeout.InfiniteTimeSpan)
        {
            ReentrantResponse = response,
        };

    private static Task<int> InvokeTask(ValueTaskTimeoutTestHarness harness, int request) =>
        harness.Invoker.InvokeAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request);

    private static ValueTask<int> InvokeOptionOffValueTask(
        ValueTaskTimeoutTestHarness harness,
        int request,
        CancellationToken cancellationToken = default) =>
        harness.Invoker.InvokeValueAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request,
            cancellationToken);

    private sealed class OperationCanceledResponseSerializer(OperationCanceledException error) :
        ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();

        public void Serialize<T>(IBufferWriter<byte> writer, T value) =>
            _inner.Serialize(writer, value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            if (typeof(T) == typeof(int))
            {
                throw error;
            }

            return _inner.Deserialize<T>(data);
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            _inner.Deserialize(data, type);
    }
}
