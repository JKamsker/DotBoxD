using System.Buffers;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

internal sealed class ValueTaskTimeoutTestHarness : IAsyncDisposable
{
    internal const string Service = "FiniteTimeout";
    internal const string Method = "Unary";
    internal const int ResponseValue = 17;
    internal const string SendFailureMessage = "Synthetic request send failed.";
    internal const string ConnectionFailureMessage = "Synthetic connection failure.";

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

    private readonly ISerializer _serializer;
    private readonly RpcStreamManager _streams;
    private readonly TaskCompletionSource _sendRelease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _cancelSent =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _blockRequestSend;
    private readonly bool _observeBlockedSendCancellation;
    private int _cancelCount;
    private int _lastRequestMessageId;
    private IPendingResponse? _lastSentPendingResponse;

    internal ValueTaskTimeoutTestHarness(
        TimeSpan requestTimeout,
        int maxPendingRequests = 1,
        bool blockRequestSend = false,
        ISerializer? serializer = null,
        bool enableLowAllocation = true,
        bool observeBlockedSendCancellation = false,
        bool useFrameSender = true)
    {
        _serializer = serializer ?? new MessagePackRpcSerializer();
        _blockRequestSend = blockRequestSend;
        _observeBlockedSendCancellation = observeBlockedSendCancellation;
        _streams = new RpcStreamManager(_serializer, SendAsync, exceptionTransformer: null);
        var options = new RpcPeerOptions
        {
            EnableLowAllocationValueTaskInvocations = enableLowAllocation,
            MaxPendingRequests = maxPendingRequests,
            RequestTimeout = requestTimeout,
        };
        Invoker = useFrameSender
            ? new RpcPeerOutboundInvoker(
                _serializer,
                options,
                ensureStarted: static () => { },
                SendAsync,
                SendFrameAsync,
                _streams)
            : new RpcPeerOutboundInvoker(
                _serializer,
                options,
                ensureStarted: static () => { },
                SendAsync,
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

    internal bool TryCancelPending(
        int messageId,
        IPendingResponse pending,
        PendingCancellationKind kind) =>
        GetPendingRequests().TryCancel(messageId, pending, kind);

    internal void CompleteGeneric(int messageId, int value = ResponseValue)
        => CompleteGeneric<int>(messageId, value);

    internal void CompleteGeneric<T>(int messageId, T value)
    {
        if (!TryCompleteGeneric(messageId, value))
        {
            throw new InvalidOperationException("Synthetic response was not accepted.");
        }
    }

    internal bool TryCompleteGeneric<T>(int messageId, T value)
    {
        var payloadWriter = new ArrayBufferWriter<byte>();
        _serializer.Serialize(payloadWriter, value);
        return TryComplete(messageId, isSuccess: true, payloadWriter.WrittenSpan);
    }

    internal void CompleteNoResponse(int messageId) =>
        Complete(messageId, isSuccess: true, ReadOnlySpan<byte>.Empty);

    internal bool TryCompleteNoResponse(int messageId) =>
        TryComplete(messageId, isSuccess: true, ReadOnlySpan<byte>.Empty);

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
        if (!MessageFramer.TryReadFrameHeader(data, out var messageId, out var messageType))
        {
            return Task.CompletedTask;
        }

        if (messageType == MessageType.Cancel)
        {
            Interlocked.Increment(ref _cancelCount);
            _cancelSent.TrySetResult(messageId);
            return Task.CompletedTask;
        }

        if (messageType != MessageType.Request)
        {
            return Task.CompletedTask;
        }

        RecordRequest(messageId);
        return GetRequestSendTask(cancellationToken);
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

            RecordRequest(messageId);
        }
        finally
        {
            frame.Dispose();
        }

        return new ValueTask(GetRequestSendTask(cancellationToken));
    }

    private void RecordRequest(int messageId)
    {
        Volatile.Write(ref _lastRequestMessageId, messageId);
        Volatile.Write(ref _lastSentPendingResponse, GetPendingResponse(messageId));
        CompleteReentrantly(messageId);
    }

    private Task GetRequestSendTask(CancellationToken cancellationToken)
    {
        if (FailRequestSend)
        {
            return Task.FromException(new InvalidOperationException(SendFailureMessage));
        }

        if (!_blockRequestSend)
        {
            return Task.CompletedTask;
        }

        return _observeBlockedSendCancellation
            ? _sendRelease.Task.WaitAsync(cancellationToken)
            : _sendRelease.Task;
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
            case ReentrantResponseKind.ConnectionFailure:
                Invoker.FailPending(new ServiceConnectionException(ConnectionFailureMessage));
                return;
            default:
                throw new InvalidOperationException("Unknown reentrant response kind.");
        }
    }

    private void Complete(int messageId, bool isSuccess, ReadOnlySpan<byte> payload)
    {
        if (!TryComplete(messageId, isSuccess, payload))
        {
            throw new InvalidOperationException("Synthetic response was not accepted.");
        }
    }

    private bool TryComplete(int messageId, bool isSuccess, ReadOnlySpan<byte> payload)
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
            return false;
        }

        return true;
    }

    private static FieldInfo RequireField(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Could not find {type.Name}.{name}.");

    private PendingRequests GetPendingRequests() =>
        (PendingRequests)InvokerPendingField.GetValue(Invoker)!;
}

internal enum ReentrantResponseKind
{
    None,
    Success,
    Error,
    ConnectionFailure,
}
