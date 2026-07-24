using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal class PendingUnaryResponse<TResponse> :
    TaskCompletionSource<TResponse>,
    IPendingResponse
{
    private RpcPeerOutboundInvoker? _directOwner;
    private PendingDirectCompletionHandshake _directCompletion;

    public PendingUnaryResponse(int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }

    public virtual long TimeoutDeadline => long.MaxValue;

    public virtual PendingCancellationKind CancellationKind => PendingCancellationKind.None;

    public bool RegistersStreamingResponse => false;

    public virtual void SetTimeoutDeadline(long deadline)
    {
    }

    public virtual void CancelByCaller()
    {
    }

    public void DisposeResultWhenAvailable()
    {
    }

    public void SetError(Exception error) =>
        CompleteAndSetException(error);

    public void EnableDirectCompletion(RpcPeerOutboundInvoker owner)
    {
        if (Interlocked.CompareExchange(ref _directOwner, owner, null) is not null)
        {
            throw new InvalidOperationException("Direct pending owner was already published.");
        }

        NotifyDirectOwner(_directCompletion.PublishOwner());
    }

    public bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        RpcFrame frame,
        RpcStreamReceiver? stream,
        ISerializer serializer)
    {
        try
        {
            if (!response.IsSuccess)
            {
                throw new RemoteServiceException(
                    response.ErrorMessage ?? "Unknown error",
                    response.ErrorType ?? "Unknown");
            }

            if (response.Stream is not null)
            {
                throw new ServiceProtocolException(
                    "Response opened a stream for a non-streaming invocation.");
            }

            var result = serializer.Deserialize<TResponse>(payload);
            if (TryCancelIfCallerCanceledAfterMaterialization())
            {
                return true;
            }

            CompleteAndSetResult(result);
        }
        catch (Exception ex)
        {
            CompleteAndSetException(ex);
        }
        finally
        {
            stream?.Cancel();
            frame.Dispose();
        }

        return true;
    }

    public virtual void TrySetCanceled(PendingCancellationKind kind)
        => TryCompleteCanceled(kind);

    protected bool TryCompleteCanceled(PendingCancellationKind kind)
    {
        // Pending-send and caller-cancelable invocations retain wrapper ownership. Their wrapper
        // translates the canceled Task into the public timeout shape and sends the cancel frame.
        if (Volatile.Read(ref _directOwner) is null)
        {
            return TrySetCanceled();
        }

        PublishDirectCompletion(sendCancel: true);
        if (kind == PendingCancellationKind.Timeout)
        {
            return TrySetException(CreateTimeoutException());
        }

        return TrySetCanceled();
    }

    protected virtual Exception CreateTimeoutException()
    {
        RpcTelemetry.RequestTimedOut();
        return new ServiceTimeoutException("Request timed out.");
    }

    protected virtual bool TryCancelIfCallerCanceledAfterMaterialization() =>
        false;

    private void CompleteAndSetResult(TResponse response)
    {
        PublishDirectCompletion(sendCancel: false);
        TrySetResult(response);
    }

    private void CompleteAndSetException(Exception error)
    {
        PublishDirectCompletion(sendCancel: false);
        TrySetException(error);
    }

    private void PublishDirectCompletion(bool sendCancel) =>
        NotifyDirectOwner(_directCompletion.PublishCompletion(sendCancel));

    private void NotifyDirectOwner(PendingDirectCompletionAction action)
    {
        if (action == PendingDirectCompletionAction.None)
        {
            return;
        }

        var owner = Volatile.Read(ref _directOwner)
            ?? throw new InvalidOperationException("Direct pending owner was not published.");
        owner.CompleteUnaryPending(
            this,
            action == PendingDirectCompletionAction.ReleaseAndSendCancel);
    }
}

internal class CancellablePendingUnaryResponse<TResponse> :
    PendingUnaryResponse<TResponse>
{
    private readonly PendingRequests _owner;
    private readonly CancellationToken _callerToken;
    private int _cancellationKind;

    public CancellablePendingUnaryResponse(PendingRequests owner, int messageId, CancellationToken callerToken)
        : base(messageId)
    {
        _owner = owner;
        _callerToken = callerToken;
    }

    public override PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public override void CancelByCaller()
        => _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    public override void TrySetCanceled(PendingCancellationKind kind)
    {
        if (!TrySetCancellationKind(kind))
        {
            return;
        }

        if (!TryCompleteCanceled(kind))
        {
            ResetCancellationKind(kind);
        }
    }

    protected override bool TryCancelIfCallerCanceledAfterMaterialization()
    {
        if (!_callerToken.IsCancellationRequested)
        {
            return false;
        }

        TrySetCanceled(PendingCancellationKind.Caller);
        return true;
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

internal sealed class PendingUnaryResponseWithTimeout<TResponse> :
    CancellablePendingUnaryResponse<TResponse>
{
    private readonly string _service;
    private readonly string _method;
    private long _timeoutDeadline = long.MaxValue;

    public PendingUnaryResponseWithTimeout(
        PendingRequests owner,
        int messageId,
        string service,
        string method,
        CancellationToken callerToken)
        : base(owner, messageId, callerToken)
    {
        _service = service;
        _method = method;
    }

    public override long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public override void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    protected override Exception CreateTimeoutException()
    {
        RpcTelemetry.RequestTimedOut();
        return new ServiceTimeoutException($"Request to {_service}.{_method} timed out.");
    }
}
