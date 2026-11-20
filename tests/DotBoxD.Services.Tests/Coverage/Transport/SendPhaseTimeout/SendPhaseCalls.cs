using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.SendPhaseTimeout;

public enum SendPhaseCall
{
    TaskUnary,
    TaskNoResponse,
    PooledUnary,
    PooledNoResponse,
    StreamingRequest,
}

internal static class SendPhaseCalls
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(80);
    internal static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    internal const string TimeoutTarget =
        $"{ValueTaskTimeoutTestHarness.Service}.{ValueTaskTimeoutTestHarness.Method}";

    internal static readonly SendPhaseCall[] AllCalls =
    [
        SendPhaseCall.TaskUnary,
        SendPhaseCall.TaskNoResponse,
        SendPhaseCall.PooledUnary,
        SendPhaseCall.PooledNoResponse,
        SendPhaseCall.StreamingRequest,
    ];

    internal static ValueTaskTimeoutTestHarness CreateBlockedHarness(
        bool useFrameSender,
        int maxPendingRequests = 1,
        TimeSpan? requestTimeout = null) =>
        new(
            requestTimeout ?? RequestTimeout,
            maxPendingRequests,
            blockRequestSend: true,
            useFrameSender: useFrameSender);

    internal static Task Invoke(
        ValueTaskTimeoutTestHarness harness,
        SendPhaseCall call,
        CancellationToken cancellationToken = default) =>
        call switch
        {
            SendPhaseCall.TaskUnary => harness.Invoker.InvokeAsync<int, int>(
                ValueTaskTimeoutTestHarness.Service,
                ValueTaskTimeoutTestHarness.Method,
                request: 1,
                cancellationToken),
            SendPhaseCall.TaskNoResponse => harness.Invoker.InvokeAsync(
                ValueTaskTimeoutTestHarness.Service,
                ValueTaskTimeoutTestHarness.Method,
                request: 1,
                cancellationToken),
            SendPhaseCall.PooledUnary => harness.Invoker.InvokeValueAsync<int, int>(
                ValueTaskTimeoutTestHarness.Service,
                ValueTaskTimeoutTestHarness.Method,
                request: 1,
                cancellationToken).AsTask(),
            SendPhaseCall.PooledNoResponse => harness.Invoker.InvokeValueAsync(
                ValueTaskTimeoutTestHarness.Service,
                ValueTaskTimeoutTestHarness.Method,
                request: 1,
                cancellationToken).AsTask(),
            SendPhaseCall.StreamingRequest => InvokeStreaming(
                harness,
                new TrackingSourceStream(),
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Unknown call kind."),
        };

    internal static Task<int> InvokeStreaming(
        ValueTaskTimeoutTestHarness harness,
        TrackingSourceStream source,
        CancellationToken cancellationToken = default)
    {
        var handle = harness.Invoker.ReserveStream(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(handle, source, leaveOpen: false);
        return harness.Invoker.InvokeAsync<int, int>(
            ValueTaskTimeoutTestHarness.Service,
            ValueTaskTimeoutTestHarness.Method,
            request: 1,
            attachment,
            cancellationToken);
    }

    internal static void CompleteLastRequest(ValueTaskTimeoutTestHarness harness, SendPhaseCall call)
    {
        if (call is SendPhaseCall.TaskNoResponse or SendPhaseCall.PooledNoResponse)
        {
            harness.CompleteNoResponse(harness.LastRequestMessageId);
            return;
        }

        harness.CompleteGeneric(harness.LastRequestMessageId);
    }

    // A detached send releases its admission slot from the send's completion continuation, so a
    // follow-up may briefly still see the slot as held after the send was released.
    internal static async Task InvokeAndCompleteWhenAdmittedAsync(
        ValueTaskTimeoutTestHarness harness,
        SendPhaseCall call)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (true)
        {
            var followUp = Invoke(harness, call);
            if (!IsPendingLimitRejection(followUp))
            {
                CompleteLastRequest(harness, call);
                await followUp.WaitAsync(Guard);
                return;
            }

            await AssertPendingLimitAsync(followUp);
            Assert.True(DateTime.UtcNow < deadline, "The retained admission slot was never released.");
            await Task.Delay(5);
        }
    }

    private static bool IsPendingLimitRejection(Task call) =>
        call.IsFaulted &&
        call.Exception!.InnerException is ServiceException { Message: "Maximum pending requests reached." };

    internal static async Task AssertPendingLimitAsync(Task call)
    {
        var error = await Assert.ThrowsAsync<ServiceException>(() => call.WaitAsync(Guard));
        Assert.Equal("Maximum pending requests reached.", error.Message);
    }

    internal static async Task AssertTimedOutAsync(Task call)
    {
        var error = await Assert.ThrowsAsync<ServiceTimeoutException>(() => call.WaitAsync(Guard));
        Assert.Contains(TimeoutTarget, error.Message);
        Assert.False(call.IsCanceled);
    }
}

internal sealed class TrackingSourceStream : MemoryStream
{
    private readonly TaskCompletionSource _disposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _readCount;

    public Task Disposed => _disposed.Task;

    public int ReadCount => Volatile.Read(ref _readCount);

    public override int Read(byte[] buffer, int offset, int count)
    {
        Interlocked.Increment(ref _readCount);
        return base.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        Interlocked.Increment(ref _readCount);
        return base.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _readCount);
        return base.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _readCount);
        return base.ReadAsync(buffer, offset, count, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed.TrySetResult();
        base.Dispose(disposing);
    }
}
