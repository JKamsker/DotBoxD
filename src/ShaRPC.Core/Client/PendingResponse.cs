namespace ShaRPC.Core.Client;

internal enum PendingCancellationKind
{
    None,
    Caller,
    Timeout,
}

internal sealed class PendingResponse : TaskCompletionSource<ReceivedResponse>
{
    private readonly ShaRpcPendingRequests _owner;
    private long _timeoutDeadline = long.MaxValue;
    private int _cancellationKind;

    public PendingResponse(ShaRpcPendingRequests owner, int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _owner = owner;
        MessageId = messageId;
    }

    public int MessageId { get; }

    public long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    public void CancelByCaller() =>
        _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    private void SetCancellationKind(PendingCancellationKind kind) =>
        Volatile.Write(ref _cancellationKind, (int)kind);

    public void TrySetCanceled(PendingCancellationKind kind)
    {
        SetCancellationKind(kind);
        TrySetCanceled();
    }
}
