using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal abstract class PooledPendingResponse : IPendingResponse
{
    private RpcPeerOutboundInvoker? _directOwner;
    private string? _service;
    private string? _method;
    private long _timeoutDeadline = long.MaxValue;
    private PooledPendingLifecycle _lifecycle;

    public int MessageId { get; private set; }

    public long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public PendingCancellationKind CancellationKind => _lifecycle.CancellationKind;

    public bool RegistersStreamingResponse => false;

    internal bool CompletionStarted => _lifecycle.CompletionStarted;

    public void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    public void CancelByCaller()
    {
    }

    public void DisposeResultWhenAvailable() =>
        _lifecycle.MarkAbandoned(this);

    public void SetError(Exception error)
    {
        var start = _lifecycle.BeginCompletion(this, PooledCompletionKind.Normal);
        if (start == PooledCompletionStart.Rejected)
        {
            return;
        }

        SetExceptionCore(error);
        _lifecycle.FinishCompletion(this, PooledCompletionKind.Normal, start);
    }

    public abstract bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer);

    public void TrySetCanceled(PendingCancellationKind kind)
    {
        var (completionKind, error) = kind switch
        {
            PendingCancellationKind.Caller => (
                PooledCompletionKind.Caller,
                (Exception)new OperationCanceledException()),
            PendingCancellationKind.Timeout => (
                PooledCompletionKind.Timeout,
                new ServiceTimeoutException($"Request to {_service}.{_method} timed out.")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Cancellation kind is not supported.")
        };

        var start = _lifecycle.BeginCompletion(this, completionKind);
        if (start == PooledCompletionStart.Rejected)
        {
            return;
        }

        if (kind == PendingCancellationKind.Timeout)
        {
            RpcTelemetry.RequestTimedOut();
        }

        SetExceptionCore(error);
        _lifecycle.FinishCompletion(this, completionKind, start);
    }

    internal void Initialize(int messageId, string service, string method)
    {
        MessageId = messageId;
        _service = service;
        _method = method;
        _directOwner = null;
        _timeoutDeadline = long.MaxValue;
        _lifecycle.Initialize();
    }

    internal void TransferSetupToWrapper() =>
        _lifecycle.TransferSetupToWrapper(this);

    internal void ReleaseSetup() =>
        _lifecycle.ReleaseSetup(this);

    internal void ReleaseWrapper() =>
        _lifecycle.ReleaseWrapper(this);

    internal void AbandonUnpublished()
    {
        DisposeResultWhenAvailable();
        CompleteAbandonedAfterRemoval();
        ReleaseSetup();
    }

    internal void CompleteAbandonedAfterRemoval()
    {
        var error = new ServiceConnectionException("Request abandoned.");
        var start = _lifecycle.BeginCompletion(this, PooledCompletionKind.Normal);
        if (start == PooledCompletionStart.Rejected)
        {
            return;
        }

        SetExceptionCore(error);
        _lifecycle.FinishCompletion(this, PooledCompletionKind.Normal, start);
    }

    internal void NotifyDirectOwner(bool sendCancel)
    {
        var owner = Volatile.Read(ref _directOwner)
            ?? throw new InvalidOperationException("Direct pending owner was not published.");
        owner.CompleteUnaryPending(this, sendCancel);
    }

    internal void RecycleClaimed()
    {
        ResetSourceCore();
        MessageId = 0;
        _timeoutDeadline = long.MaxValue;
        _service = null;
        _method = null;
        _directOwner = null;
        PushToPoolCore();
    }

    protected void MarkIssued() =>
        _lifecycle.MarkIssued(this);

    protected void MarkIssuedForDirect(RpcPeerOutboundInvoker owner)
    {
        Volatile.Write(ref _directOwner, owner);
        _lifecycle.MarkIssuedForDirect(this);
    }

    protected void MarkConsumed() =>
        _lifecycle.MarkConsumed(this);

    protected PooledCompletionStart BeginResultCompletion() =>
        _lifecycle.BeginCompletion(this, PooledCompletionKind.Normal);

    protected void FinishResultCompletion(PooledCompletionStart start) =>
        _lifecycle.FinishCompletion(this, PooledCompletionKind.Normal, start);

    protected abstract void SetExceptionCore(Exception error);

    protected abstract void ResetSourceCore();

    protected abstract void PushToPoolCore();
}
