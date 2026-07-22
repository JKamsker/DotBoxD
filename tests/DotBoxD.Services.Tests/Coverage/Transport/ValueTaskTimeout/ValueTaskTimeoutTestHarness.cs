using System.Buffers;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

internal sealed class ValueTaskTimeoutTestHarness : IAsyncDisposable
{
    internal const string Service = "FiniteTimeout";
    internal const string Method = "Unary";
    internal const int ResponseValue = 17;
    internal const string SendFailureMessage = "Synthetic request send failed.";

    // Inspect the reserved response while holding PendingRequests' own gate. This pins the optimized
    // implementation choice without depending on private ValueTask runtime fields.
    private static readonly FieldInfo InvokerPendingField = RequireField(
        typeof(RpcPeerOutboundInvoker),
        "_pending");
    private static readonly FieldInfo PendingRequestsField = RequireField(
        typeof(PendingRequests),
        "_requests");
    private static readonly FieldInfo PendingRequestsGateField = RequireField(
        typeof(PendingRequests),
        "_requestsGate");

    private readonly MessagePackRpcSerializer _serializer = new();
    private readonly RpcStreamManager _streams;
    private readonly TaskCompletionSource _sendRelease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _cancelSent =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _blockRequestSend;
    private int _cancelCount;
    private int _lastRequestMessageId;
    private IPendingResponse? _lastSentPendingResponse;

    internal ValueTaskTimeoutTestHarness(
        TimeSpan requestTimeout,
        int maxPendingRequests = 1,
        bool blockRequestSend = false)
    {
        _blockRequestSend = blockRequestSend;
        _streams = new RpcStreamManager(_serializer, SendAsync, exceptionTransformer: null);
        Invoker = new RpcPeerOutboundInvoker(
            _serializer,
            new RpcPeerOptions
            {
                EnableLowAllocationValueTaskInvocations = true,
                MaxPendingRequests = maxPendingRequests,
                RequestTimeout = requestTimeout,
            },
            ensureStarted: static () => { },
            SendAsync,
            SendFrameAsync,
            _streams);
    }

    internal RpcPeerOutboundInvoker Invoker { get; }

    internal ReentrantResponseKind ReentrantResponse { get; set; }

    internal bool FailRequestSend { get; set; }

    internal int LastRequestMessageId => Volatile.Read(ref _lastRequestMessageId);

    internal IPendingResponse LastSentPendingResponse =>
        Volatile.Read(ref _lastSentPendingResponse)
        ?? throw new InvalidOperationException("No request has been sent.");

    internal int CancelCount => Volatile.Read(ref _cancelCount);

    internal IPendingResponse GetPendingResponse(int messageId)
    {
        var pending = (PendingRequests)InvokerPendingField.GetValue(Invoker)!;
        var requests = (Dictionary<int, IPendingResponse>)PendingRequestsField.GetValue(pending)!;
        var gate = PendingRequestsGateField.GetValue(pending)!;
        lock (gate)
        {
            return requests[messageId];
        }
    }

    internal void CompleteGeneric(int messageId, int value = ResponseValue)
        => CompleteGeneric<int>(messageId, value);

    internal void CompleteGeneric<T>(int messageId, T value)
    {
        var payloadWriter = new ArrayBufferWriter<byte>();
        _serializer.Serialize(payloadWriter, value);
        Complete(messageId, isSuccess: true, payloadWriter.WrittenSpan);
    }

    internal void CompleteNoResponse(int messageId) =>
        Complete(messageId, isSuccess: true, ReadOnlySpan<byte>.Empty);

    internal void CompleteError(int messageId) =>
        Complete(messageId, isSuccess: false, ReadOnlySpan<byte>.Empty);

    internal void ReleaseRequestSend() =>
        _sendRelease.TrySetResult();

    internal async Task<int> WaitForCancelAsync(TimeSpan timeout) =>
        await _cancelSent.Task.WaitAsync(timeout).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        ReleaseRequestSend();
        Invoker.FailPending(new ServiceConnectionException("Test harness disposed."));
        await Invoker.StopCancelFramesAsync().ConfigureAwait(false);
        _streams.Stop();
    }

    private Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (MessageFramer.TryReadFrameHeader(data, out var messageId, out var messageType) &&
            messageType == MessageType.Cancel)
        {
            Interlocked.Increment(ref _cancelCount);
            _cancelSent.TrySetResult(messageId);
        }

        return Task.CompletedTask;
    }

    private ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!MessageFramer.TryReadFrameHeader(frame.WrittenMemory, out var messageId, out var messageType) ||
                messageType != MessageType.Request)
            {
                throw new InvalidOperationException("Expected an outbound request frame.");
            }

            Volatile.Write(ref _lastRequestMessageId, messageId);
            Volatile.Write(ref _lastSentPendingResponse, GetPendingResponse(messageId));
            CompleteReentrantly(messageId);
        }
        finally
        {
            frame.Dispose();
        }

        if (FailRequestSend)
        {
            return new ValueTask(Task.FromException(new InvalidOperationException(SendFailureMessage)));
        }

        return _blockRequestSend ? new ValueTask(_sendRelease.Task) : default;
    }

    private void CompleteReentrantly(int messageId)
    {
        switch (ReentrantResponse)
        {
            case ReentrantResponseKind.None:
                return;
            case ReentrantResponseKind.Success:
                CompleteGeneric(messageId);
                return;
            case ReentrantResponseKind.Error:
                Complete(messageId, isSuccess: false, ReadOnlySpan<byte>.Empty);
                return;
            default:
                throw new InvalidOperationException("Unknown reentrant response kind.");
        }
    }

    private void Complete(int messageId, bool isSuccess, ReadOnlySpan<byte> payload)
    {
        var response = MessageFramer.FrameMessage(
            _serializer,
            messageId,
            isSuccess ? MessageType.Response : MessageType.Error,
            new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = isSuccess,
                ErrorMessage = isSuccess ? null : "remote failure",
                ErrorType = isSuccess ? null : "RemoteFailure",
            },
            payload);

        if (!Invoker.TryCompleteResponse(messageId, response))
        {
            response.Dispose();
            throw new InvalidOperationException("Synthetic response was not accepted.");
        }
    }

    private static FieldInfo RequireField(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Could not find {type.Name}.{name}.");
}

internal enum ReentrantResponseKind
{
    None,
    Success,
    Error,
}
