using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class PendingResponseWrapperHarness : IDisposable
{
    public const int MaxPendingRequests = 1;
    public const int ResponseValue = 17;

    private const string ServiceName = "Probe";
    private const string MethodName = "Call";

    private readonly RpcStreamManager _streams;
    private readonly ReusablePendingResponseFrameSender _sender;
    private long _fallbackMemorySends;
    private long _followUpCalls;
    private long _invocations;
    private long _pooledNoResponseInvocations;
    private long _pooledUnaryInvocations;
    private long _resultChecksum;
    private long _taskNoResponseInvocations;
    private long _taskUnaryInvocations;

    public PendingResponseWrapperHarness(bool forcePendingSend)
    {
        ISerializer serializer = new MessagePackRpcSerializer();
        var responsePayloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(responsePayloadWriter, ResponseValue);
        _sender = new ReusablePendingResponseFrameSender(
            serializer,
            responsePayloadWriter.WrittenSpan.ToArray(),
            forcePendingSend);
        _streams = new RpcStreamManager(serializer, SendMemoryAsync, exceptionTransformer: null);
        Invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                MaxPendingRequests = MaxPendingRequests,
                RequestTimeout = Timeout.InfiniteTimeSpan,
            },
            ensureStarted: static () => { },
            SendMemoryAsync,
            _sender.SendFrameAsync,
            _streams);
        _sender.Attach(Invoker);
    }

    private RpcPeerOutboundInvoker Invoker { get; }

    public long AcceptedResponses => _sender.AcceptedResponseCount;

    public long FollowUpCalls => _followUpCalls;

    public long InvocationCount => _invocations;

    public long MessageIdChecksum => _sender.Snapshot().MessageIdChecksum;

    public long ResultChecksum => _resultChecksum;

    public long SourceReuseCycles => _sender.SourceResetCount;

    public int InvokeOnce(PendingInvocationKind kind)
    {
        var result = kind switch
        {
            PendingInvocationKind.PooledUnary => InvokePooledUnaryOnce(),
            PendingInvocationKind.PooledNoResponse => InvokePooledNoResponseOnce(),
            PendingInvocationKind.TaskUnary => InvokeTaskUnaryOnce(),
            PendingInvocationKind.TaskNoResponse => InvokeTaskNoResponseOnce(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown invocation kind."),
        };

        _invocations++;
        _resultChecksum += result;
        switch (kind)
        {
            case PendingInvocationKind.PooledUnary:
                _pooledUnaryInvocations++;
                break;
            case PendingInvocationKind.PooledNoResponse:
                _pooledNoResponseInvocations++;
                break;
            case PendingInvocationKind.TaskUnary:
                _taskUnaryInvocations++;
                break;
            case PendingInvocationKind.TaskNoResponse:
                _taskNoResponseInvocations++;
                break;
        }

        return result;
    }

    public void VerifyFollowUpCapacity(PendingInvocationKind kind)
    {
        var result = InvokeOnce(kind);
        var expected = IsUnary(kind) ? ResponseValue : 1;
        if (result != expected)
        {
            throw new InvalidOperationException(
                $"Follow-up call returned {result}; expected {expected}.");
        }

        _followUpCalls++;
    }

    public void VerifyTotals(long pooledCallsPerShape, long taskCallsPerShape)
    {
        var totalCalls = checked((pooledCallsPerShape + taskCallsPerShape) * 2);
        var sender = _sender.Snapshot();
        var expectedPendingSends = _sender.ForcePendingSend ? totalCalls : 0;
        var expectedSynchronousSends = _sender.ForcePendingSend ? 0 : totalCalls;
        var expectedMessageIdChecksum = checked(totalCalls * (totalCalls + 1) / 2);
        var expectedResultChecksum = checked(
            (pooledCallsPerShape + taskCallsPerShape) * (ResponseValue + 1L));
        var expectedFollowUps = taskCallsPerShape == 0 ? 2 : 4;
        if (_invocations != totalCalls ||
            _pooledUnaryInvocations != pooledCallsPerShape ||
            _pooledNoResponseInvocations != pooledCallsPerShape ||
            _taskUnaryInvocations != taskCallsPerShape ||
            _taskNoResponseInvocations != taskCallsPerShape ||
            _resultChecksum != expectedResultChecksum ||
            _followUpCalls != expectedFollowUps ||
            _fallbackMemorySends != 0 ||
            sender.FrameSendCount != totalCalls ||
            sender.PendingSendCount != expectedPendingSends ||
            sender.SynchronousSendCount != expectedSynchronousSends ||
            sender.SendCompletionCount != expectedPendingSends ||
            sender.SendResultReadCount != expectedPendingSends ||
            sender.SourceResetCount != expectedPendingSends ||
            sender.DisposedWriterCount != totalCalls ||
            sender.ResponseAttemptCount != totalCalls ||
            sender.AcceptedResponseCount != totalCalls ||
            sender.MessageIdChecksum != expectedMessageIdChecksum ||
            !sender.IsIdle)
        {
            throw new InvalidOperationException(
                "Pending-response wrapper harness counters did not match the expected lifecycle.");
        }
    }

    public static bool IsUnary(PendingInvocationKind kind) =>
        kind is PendingInvocationKind.PooledUnary or PendingInvocationKind.TaskUnary;

    public void Dispose()
    {
        if (!_sender.Snapshot().IsIdle)
        {
            throw new InvalidOperationException("The reusable send source is still active.");
        }

        Invoker.FailPending(new InvalidOperationException("Probe disposed."));
        Invoker.StopCancelFramesAsync().GetAwaiter().GetResult();
        _streams.Stop();
    }

    private int InvokePooledUnaryOnce()
    {
        _sender.Prepare(PendingResponseShape.Unary);
        var call = Invoker.InvokeValueAsync<int>(ServiceName, MethodName);
        EnsureInitiallyPending(call.IsCompleted);
        _sender.CompleteSendAndResponse();
        if (!call.IsCompletedSuccessfully || call.Result != ResponseValue)
        {
            throw new InvalidOperationException("The pooled unary response did not complete successfully.");
        }

        return ResponseValue;
    }

    private int InvokePooledNoResponseOnce()
    {
        _sender.Prepare(PendingResponseShape.NoResponse);
        var call = Invoker.InvokeValueAsync(ServiceName, MethodName);
        EnsureInitiallyPending(call.IsCompleted);
        _sender.CompleteSendAndResponse();
        if (!call.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The pooled no-response call did not complete successfully.");
        }

        call.GetAwaiter().GetResult();
        return 1;
    }

    private int InvokeTaskUnaryOnce()
    {
        _sender.Prepare(PendingResponseShape.Unary);
        var call = Invoker.InvokeAsync<int>(ServiceName, MethodName);
        EnsureInitiallyPending(call.IsCompleted);
        _sender.CompleteSendAndResponse();
        if (!call.IsCompletedSuccessfully || call.Result != ResponseValue)
        {
            throw new InvalidOperationException("The Task unary response did not complete successfully.");
        }

        return ResponseValue;
    }

    private int InvokeTaskNoResponseOnce()
    {
        _sender.Prepare(PendingResponseShape.NoResponse);
        var call = Invoker.InvokeAsync(ServiceName, MethodName);
        EnsureInitiallyPending(call.IsCompleted);
        _sender.CompleteSendAndResponse();
        if (!call.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The Task no-response call did not complete successfully.");
        }

        call.GetAwaiter().GetResult();
        return 1;
    }

    private static void EnsureInitiallyPending(bool isCompleted)
    {
        if (isCompleted)
        {
            throw new InvalidOperationException(
                "The invocation completed before the synthetic response was delivered.");
        }
    }

    private Task SendMemoryAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _fallbackMemorySends++;
        return Task.CompletedTask;
    }
}

internal enum PendingResponseShape
{
    Unary,
    NoResponse,
}

internal enum PendingInvocationKind
{
    PooledUnary,
    PooledNoResponse,
    TaskUnary,
    TaskNoResponse,
}
