using System.Diagnostics;
namespace DotBoxD.Services.Client;

internal sealed class PendingRequests : IDisposable
{
    private readonly object _requestsGate = new();
    private readonly Dictionary<int, IPendingResponse> _requests = new();
    private readonly PendingRequestTimeoutScheduler _timeouts;

    public PendingRequests()
    {
        _timeouts = new PendingRequestTimeoutScheduler(CancelExpired);
    }

    public int Count { get { lock (_requestsGate) { return _requests.Count; } } }

    public bool TryAdd(int messageId, out PendingReceivedResponse pending) =>
        TryAddCore(messageId, new PendingReceivedResponse(this, messageId), out pending);
    public bool TryAddUnary<TResponse>(
        int messageId,
        bool captureCallerCancellation,
        bool captureTimeoutTarget,
        CancellationToken callerToken,
        string service,
        string method,
        out PendingUnaryResponse<TResponse> pending)
    {
        var candidate = captureTimeoutTarget
            ? new PendingUnaryResponseWithTimeout<TResponse>(this, messageId, service, method, callerToken)
            : captureCallerCancellation
                ? new CancellablePendingUnaryResponse<TResponse>(this, messageId, callerToken)
                : new PendingUnaryResponse<TResponse>(messageId);

        return TryAddCore(messageId, candidate, out pending);
    }

    public bool TryAddValueTaskUnary<TResponse>(
        int messageId,
        string service,
        string method,
        RpcPeerOutboundInvoker owner,
        CancellationToken callerToken,
        out PendingValueTaskUnaryResponse<TResponse> pending)
    {
        var candidate = PendingValueTaskUnaryResponse<TResponse>.Rent(
            messageId,
            service,
            method,
            owner,
            callerToken);
        var added = TryAddCore(messageId, candidate, out pending);
        if (!added)
            candidate.AbandonUnpublished();
        return added;
    }

    public bool TryAddValueTaskNoResponse(
        int messageId,
        string service,
        string method,
        RpcPeerOutboundInvoker owner,
        CancellationToken callerToken,
        out PendingValueTaskNoResponse pending)
    {
        var candidate = PendingValueTaskNoResponse.Rent(
            messageId,
            service,
            method,
            owner,
            callerToken);
        var added = TryAddCore(messageId, candidate, out pending);
        if (!added)
            candidate.AbandonUnpublished();
        return added;
    }

    private bool TryAddCore<TPending>(int messageId, TPending candidate, out TPending pending)
        where TPending : IPendingResponse
    {
        lock (_requestsGate)
        {
            if (_requests.TryAdd(messageId, candidate))
            {
                pending = candidate;
                return true;
            }
        }

        pending = default!;
        return false;
    }

    public bool Remove(int messageId, IPendingResponse pending, bool consumed)
    {
        var removed = TryRemove(messageId, pending);
        if (!consumed)
        {
            pending.DisposeResultWhenAvailable();
        }

        return removed;
    }

    public bool TryTake(int messageId, out IPendingResponse pending)
    {
        lock (_requestsGate)
        {
            return _requests.Remove(messageId, out pending!);
        }
    }

    public void StartTimeout(IPendingResponse pending, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        var deadline = PendingRequestTimeoutScheduler.GetDeadline(timeout);
        pending.SetTimeoutDeadline(deadline);
        _timeouts.Schedule(deadline);
    }

    public bool TryFail(int messageId, Exception error)
    {
        if (!TryTake(messageId, out var completion))
        {
            return false;
        }

        completion.SetError(error);
        return true;
    }

    /// <summary>
    /// Atomically removes the pending request and cancels it. Returns <see langword="false"/> when the
    /// entry was already removed (for example, a response completed it first), making the caller a
    /// no-op. This lets a timeout and a response race on a single removal so a delivered response is
    /// never discarded as a spurious cancellation.
    /// </summary>
    public bool TryCancel(
        int messageId,
        IPendingResponse pending,
        PendingCancellationKind kind)
    {
        if (!TryRemove(messageId, pending))
        {
            return false;
        }

        pending.TrySetCanceled(kind);
        return true;
    }

    public void FailAll(Exception error)
    {
        IPendingResponse[] pending;
        lock (_requestsGate)
        {
            if (_requests.Count == 0)
            {
                return;
            }

            pending = new IPendingResponse[_requests.Count];
            _requests.Values.CopyTo(pending, 0);
            _requests.Clear();
        }

        foreach (var request in pending)
        {
            request.SetError(error);
        }
    }

    public void Dispose()
    {
        _timeouts.Dispose();
    }

    private bool TryRemove(int messageId, IPendingResponse pending)
    {
        lock (_requestsGate)
        { return TryRemoveCore(messageId, pending); }
    }

    private bool TryRemoveCore(int messageId, IPendingResponse pending)
    {
        if (!_requests.TryGetValue(messageId, out var current) ||
            !ReferenceEquals(current, pending))
        {
            return false;
        }

        _requests.Remove(messageId);
        return true;
    }

    private void CancelExpired()
    {
        var scan = ScanExpiredRequests(Stopwatch.GetTimestamp());
        CancelExpiredResponses(scan.Expired);
        _timeouts.Reschedule(scan.Next);
    }

    private TimeoutScan ScanExpiredRequests(long now)
    {
        var next = long.MaxValue;
        List<IPendingResponse>? expired = null;
        lock (_requestsGate)
        {
            foreach (var pair in _requests)
            {
                TrackPendingDeadline(pair.Value, now, ref next, ref expired);
            }

            RemoveExpiredRequestsLocked(expired);
        }

        return new TimeoutScan(expired, next);
    }

    private static void TrackPendingDeadline(
        IPendingResponse pending,
        long now,
        ref long next,
        ref List<IPendingResponse>? expired)
    {
        var deadline = pending.TimeoutDeadline;
        if (deadline == long.MaxValue)
        {
            return;
        }

        if (deadline <= now)
        {
            expired ??= new List<IPendingResponse>();
            expired.Add(pending);
            return;
        }

        if (deadline < next)
        {
            next = deadline;
        }
    }

    private void RemoveExpiredRequestsLocked(List<IPendingResponse>? expired)
    {
        if (expired is null)
        {
            return;
        }

        for (var i = 0; i < expired.Count; i++)
        {
            var pending = expired[i];
            TryRemoveCore(pending.MessageId, pending);
        }
    }

    private static void CancelExpiredResponses(List<IPendingResponse>? expired)
    {
        if (expired is null)
        {
            return;
        }

        for (var i = 0; i < expired.Count; i++)
        {
            expired[i].TrySetCanceled(PendingCancellationKind.Timeout);
        }
    }

    private readonly record struct TimeoutScan(List<IPendingResponse>? Expired, long Next);
}
