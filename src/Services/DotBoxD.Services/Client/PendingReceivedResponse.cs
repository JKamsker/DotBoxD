using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal sealed class PendingReceivedResponse :
    TaskCompletionSource<ReceivedResponse>,
    IPendingResponse
{
    private readonly PendingRequests _owner;
    private long _timeoutDeadline = long.MaxValue;
    private int _cancellationKind;

    public PendingReceivedResponse(PendingRequests owner, int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _owner = owner;
        MessageId = messageId;
    }

    public int MessageId { get; }

    public long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public bool RegistersStreamingResponse => true;

    public void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    public void CancelByCaller() =>
        _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    public void DisposeResultWhenAvailable() =>
        ReceivedResponse.DisposeWhenAvailable(Task);

    public void SetError(Exception error) =>
        TrySetException(error);

    public bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer)
    {
        var received = new ReceivedResponse(response, payload, frame, stream);
        if (!TrySetResult(received))
        {
            received.Dispose();
        }

        return true;
    }

    public void TrySetCanceled(PendingCancellationKind kind)
    {
        if (!TrySetCancellationKind(kind))
        {
            return;
        }

        if (!TrySetCanceled())
        {
            ResetCancellationKind(kind);
        }
    }

    private bool TrySetCancellationKind(PendingCancellationKind kind) =>
        Interlocked.CompareExchange(
            ref _cancellationKind,
            (int)kind,
            (int)PendingCancellationKind.None) == (int)PendingCancellationKind.None;

    private void ResetCancellationKind(PendingCancellationKind kind) =>
        Interlocked.CompareExchange(
            ref _cancellationKind,
            (int)PendingCancellationKind.None,
            (int)kind);
}
