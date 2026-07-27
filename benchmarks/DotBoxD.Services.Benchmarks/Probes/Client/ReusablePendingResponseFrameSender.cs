using System.Threading.Tasks.Sources;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class ReusablePendingResponseFrameSender : IValueTaskSource
{
    private readonly bool _forcePendingSend;
    private readonly byte[] _responsePayload;
    private readonly ISerializer _serializer;
    private ManualResetValueTaskSourceCore<bool> _source;
    private PooledBufferWriter? _frame;
    private RpcPeerOutboundInvoker? _invoker;
    private PendingResponseShape _responseShape;
    private SenderState _state;
    private short _activeToken;
    private int _messageId;
    private long _acceptedResponseCount;
    private long _disposedWriterCount;
    private long _frameSendCount;
    private long _messageIdChecksum;
    private long _pendingSendCount;
    private long _responseAttemptCount;
    private long _sendCompletionCount;
    private long _sendResultReadCount;
    private long _sourceResetCount;
    private long _synchronousSendCount;

    public ReusablePendingResponseFrameSender(
        ISerializer serializer,
        byte[] responsePayload,
        bool forcePendingSend)
    {
        _serializer = serializer;
        _responsePayload = responsePayload;
        _forcePendingSend = forcePendingSend;
        _source.RunContinuationsAsynchronously = false;
    }

    public bool ForcePendingSend => _forcePendingSend;

    public long AcceptedResponseCount => _acceptedResponseCount;

    public long SourceResetCount => _sourceResetCount;

    public void Attach(RpcPeerOutboundInvoker invoker)
    {
        if (_invoker is not null)
        {
            throw new InvalidOperationException("The response invoker is already attached.");
        }

        _invoker = invoker;
    }

    public void Prepare(PendingResponseShape responseShape)
    {
        if (_state != SenderState.Idle)
        {
            throw new InvalidOperationException("The previous frame send is still active.");
        }

        _responseShape = responseShape;
        _state = SenderState.Prepared;
    }

    public ValueTask SendFrameAsync(
        PooledBufferWriter frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state != SenderState.Prepared || _invoker is null)
        {
            frame.Dispose();
            throw new InvalidOperationException("The reusable send source was not prepared.");
        }

        if (!MessageFramer.TryReadFrameHeader(
                frame.WrittenMemory,
                out var messageId,
                out var messageType) ||
            messageId <= 0 ||
            messageType != MessageType.Request)
        {
            frame.Dispose();
            _state = SenderState.Idle;
            throw new InvalidOperationException("Expected a positive-id outbound request frame.");
        }

        _frame = frame;
        _messageId = messageId;
        _frameSendCount++;
        _messageIdChecksum += messageId;
        if (!_forcePendingSend)
        {
            _synchronousSendCount++;
            DisposeRequestWriter();
            _state = SenderState.AwaitingResponse;
            return default;
        }

        _source.Reset();
        _activeToken = _source.Version;
        _sourceResetCount++;
        _pendingSendCount++;
        _state = SenderState.Pending;
        return new ValueTask(this, _activeToken);
    }

    public void CompleteSendAndResponse()
    {
        if (!_forcePendingSend)
        {
            if (_state != SenderState.AwaitingResponse)
            {
                throw new InvalidOperationException("No synchronous frame send is awaiting a response.");
            }

            CompleteResponse();
            return;
        }

        if (_state != SenderState.Pending ||
            _source.GetStatus(_activeToken) != ValueTaskSourceStatus.Pending)
        {
            throw new InvalidOperationException("No genuinely pending frame send is ready to complete.");
        }

        _sendCompletionCount++;
        _source.SetResult(true);
        if (_state != SenderState.Idle)
        {
            throw new InvalidOperationException(
                "The pending send continuation did not consume the source inline.");
        }
    }

    public ResponseFrameSenderSnapshot Snapshot() =>
        new(
            _state == SenderState.Idle,
            _frameSendCount,
            _pendingSendCount,
            _synchronousSendCount,
            _sendCompletionCount,
            _sendResultReadCount,
            _sourceResetCount,
            _disposedWriterCount,
            _responseAttemptCount,
            _acceptedResponseCount,
            _messageIdChecksum);

    void IValueTaskSource.GetResult(short token)
    {
        _source.GetResult(token);
        if (_state != SenderState.Pending || token != _activeToken)
        {
            throw new InvalidOperationException("A stale pending-send generation was consumed.");
        }

        _sendResultReadCount++;
        DisposeRequestWriter();
        CompleteResponse();
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) =>
        _source.GetStatus(token);

    void IValueTaskSource.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags) =>
        _source.OnCompleted(continuation, state, token, flags);

    private void DisposeRequestWriter()
    {
        var frame = _frame
            ?? throw new InvalidOperationException("The request writer was already released.");
        _frame = null;
        frame.Dispose();
        _disposedWriterCount++;
    }

    private void CompleteResponse()
    {
        var invoker = _invoker
            ?? throw new InvalidOperationException("The response invoker is not attached.");
        var messageId = _messageId;
        var payload = _responseShape == PendingResponseShape.Unary
            ? _responsePayload.AsSpan()
            : ReadOnlySpan<byte>.Empty;
        var writer = MessageFramer.RentFrameMessage(
            _serializer,
            messageId,
            MessageType.Response,
            new RpcResponse { MessageId = messageId, IsSuccess = true },
            payload);
        var accepted = false;
        try
        {
            _responseAttemptCount++;
            accepted = invoker.TryCompleteResponse(messageId, new RpcFrame(writer));
            if (!accepted)
            {
                throw new InvalidOperationException("The synthetic response was not accepted.");
            }

            _acceptedResponseCount++;
        }
        finally
        {
            if (!accepted)
            {
                writer.Dispose();
            }

            _messageId = 0;
            _state = SenderState.Idle;
        }
    }

    private enum SenderState
    {
        Idle,
        Prepared,
        Pending,
        AwaitingResponse,
    }
}

internal readonly record struct ResponseFrameSenderSnapshot(
    bool IsIdle,
    long FrameSendCount,
    long PendingSendCount,
    long SynchronousSendCount,
    long SendCompletionCount,
    long SendResultReadCount,
    long SourceResetCount,
    long DisposedWriterCount,
    long ResponseAttemptCount,
    long AcceptedResponseCount,
    long MessageIdChecksum);
