using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal abstract class PooledPendingResponse : IPendingResponse
{
    private RpcPeerOutboundInvoker? _owner;
    private CancellationToken _callerToken;
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
        var owner = Volatile.Read(ref _owner)
            ?? throw new InvalidOperationException("Pooled pending owner was not published.");
        owner.CancelPooledByCaller(this);
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
                (Exception)new OperationCanceledException(_callerToken)),
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

    internal void Initialize(
        int messageId,
        string service,
        string method,
        RpcPeerOutboundInvoker? owner,
        CancellationToken callerToken)
    {
        MessageId = messageId;
        _service = service;
        _method = method;
        _owner = owner;
        _callerToken = callerToken;
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
        var owner = Volatile.Read(ref _owner)
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
        _owner = null;
        _callerToken = default;
        PushToPoolCore();
    }

    protected void MarkIssued() =>
        _lifecycle.MarkIssued(this);

    protected void MarkIssuedForDirect(RpcPeerOutboundInvoker owner)
    {
        var publishedOwner = Volatile.Read(ref _owner);
        if (publishedOwner is null)
        {
            publishedOwner = Interlocked.CompareExchange(ref _owner, owner, null);
            publishedOwner ??= owner;
        }

        if (!ReferenceEquals(publishedOwner, owner))
        {
            throw new InvalidOperationException("Direct pending owner does not match the reserved owner.");
        }

        _lifecycle.MarkIssuedForDirect(this);
    }

    protected void MarkConsumed() =>
        _lifecycle.MarkConsumed(this);

    internal CancellationTokenRegistration RegisterCallerCancellation(CancellationToken callerToken)
    {
        if (!callerToken.CanBeCanceled)
        {
            return default;
        }

        if (callerToken != _callerToken)
        {
            throw new InvalidOperationException("Caller cancellation token does not match the reserved token.");
        }

        return callerToken.Register(
            static state => ((PooledPendingResponse)state!).CancelByCaller(),
            this);
    }

    protected void ThrowIfCallerCancellationRequested() =>
        _callerToken.ThrowIfCancellationRequested();

    protected bool TryCancelIfCallerCanceledAfterMaterialization()
    {
        if (!_callerToken.IsCancellationRequested)
        {
            return false;
        }

        TrySetCanceled(PendingCancellationKind.Caller);
        return true;
    }

    protected PooledCompletionStart BeginResultCompletion() =>
        _lifecycle.BeginCompletion(this, PooledCompletionKind.Normal);

    protected void FinishResultCompletion(PooledCompletionStart start) =>
        _lifecycle.FinishCompletion(this, PooledCompletionKind.Normal, start);

    protected abstract void SetExceptionCore(Exception error);

    protected abstract void ResetSourceCore();

    protected abstract void PushToPoolCore();
}
