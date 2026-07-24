using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;

namespace DotBoxD.Services.Client;

internal sealed class TimeoutOnlyPendingUnaryResponse<TResponse> :
    PendingUnaryResponse<TResponse>
{
    private readonly string _service;
    private readonly string _method;
    private long _timeoutDeadline = long.MaxValue;
    private int _cancellationKind;

    public TimeoutOnlyPendingUnaryResponse(int messageId, string service, string method)
        : base(messageId)
    {
        _service = service;
        _method = method;
    }

    public override long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public override PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public override void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

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

    protected override Exception CreateTimeoutException()
    {
        RpcTelemetry.RequestTimedOut();
        return new ServiceTimeoutException($"Request to {_service}.{_method} timed out.");
    }
}
