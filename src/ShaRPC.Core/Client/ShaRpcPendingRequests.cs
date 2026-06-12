using System.Diagnostics;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core.Client;

internal sealed class ShaRpcPendingRequests : IDisposable
{
    private readonly object _requestsGate = new();
    private readonly object _timeoutGate = new();
    private readonly Dictionary<int, PendingResponse> _requests = new();
    private readonly Timer _timeoutTimer;
    private long _nextTimeoutTimestamp = long.MaxValue;
    private int _disposed;

    public ShaRpcPendingRequests()
    {
        _timeoutTimer = new Timer(
            static state => ((ShaRpcPendingRequests)state!).CancelExpired(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public int Count
    {
        get
        {
            lock (_requestsGate)
            {
                return _requests.Count;
            }
        }
    }

    public bool TryAdd(int messageId, out PendingResponse pending)
    {
        pending = new PendingResponse(this, messageId);
        lock (_requestsGate)
        {
            if (!_requests.ContainsKey(messageId))
            {
                _requests.Add(messageId, pending);
                return true;
            }
        }

        pending = null!;
        return false;
    }

    public void Remove(int messageId, PendingResponse pending, bool consumed)
    {
        TryRemove(messageId, pending);
        if (!consumed)
        {
            ReceivedResponse.DisposeWhenAvailable(pending.Task);
        }
    }

    public bool TryTake(int messageId, out PendingResponse pending)
    {
        lock (_requestsGate)
        {
            if (!_requests.TryGetValue(messageId, out pending!))
            {
                return false;
            }

            _requests.Remove(messageId);
            return true;
        }
    }

    public void StartTimeout(PendingResponse pending, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        var timeoutTicks = MillisecondsToStopwatchTicks((long)Math.Ceiling(timeout.TotalMilliseconds));
        var deadline = Stopwatch.GetTimestamp() + timeoutTicks;
        pending.SetTimeoutDeadline(deadline);
        ScheduleTimeout(deadline);
    }

    public bool TryComplete(
        int messageId,
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame,
        RpcStreamReceiver? stream)
    {
        if (!TryTake(messageId, out var completion))
        {
            return false;
        }

        var received = new ReceivedResponse(response, payload, frame, stream);
        if (!completion.TrySetResult(received))
        {
            received.Dispose();
        }

        return true;
    }

    public bool TryFail(int messageId, Exception error)
    {
        if (!TryTake(messageId, out var completion))
        {
            return false;
        }

        completion.TrySetException(error);
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
        PendingResponse pending,
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
        PendingResponse[] pending;
        lock (_requestsGate)
        {
            if (_requests.Count == 0)
            {
                return;
            }

            pending = new PendingResponse[_requests.Count];
            _requests.Values.CopyTo(pending, 0);
            _requests.Clear();
        }

        foreach (var request in pending)
        {
            request.TrySetException(error);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _timeoutTimer.Dispose();
        }
    }

    private void ScheduleTimeout(long deadline)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_timeoutGate)
        {
            if (_disposed != 0 || deadline >= _nextTimeoutTimestamp)
            {
                return;
            }

            _nextTimeoutTimestamp = deadline;
            ScheduleTimerLocked();
        }
    }

    private void CancelExpired()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_timeoutGate)
        {
            _nextTimeoutTimestamp = long.MaxValue;
        }

        var now = Stopwatch.GetTimestamp();
        var next = long.MaxValue;
        List<PendingResponse>? expired = null;
        lock (_requestsGate)
        {
            foreach (var pair in _requests)
            {
                var deadline = pair.Value.TimeoutDeadline;
                if (deadline == long.MaxValue)
                {
                    continue;
                }

                if (deadline <= now)
                {
                    expired ??= new List<PendingResponse>();
                    expired.Add(pair.Value);
                }
                else if (deadline < next)
                {
                    next = deadline;
                }
            }

            if (expired is not null)
            {
                for (var i = 0; i < expired.Count; i++)
                {
                    var pending = expired[i];
                    TryRemoveCore(pending.MessageId, pending);
                }
            }
        }

        if (expired is not null)
        {
            for (var i = 0; i < expired.Count; i++)
            {
                expired[i].TrySetCanceled(PendingCancellationKind.Timeout);
            }
        }

        lock (_timeoutGate)
        {
            if (_disposed != 0)
            {
                return;
            }

            if (next < _nextTimeoutTimestamp)
            {
                _nextTimeoutTimestamp = next;
            }

            ScheduleTimerLocked();
        }
    }

    private void ScheduleTimerLocked()
    {
        if (_nextTimeoutTimestamp == long.MaxValue)
        {
            _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var remainingTicks = Math.Max(0, _nextTimeoutTimestamp - Stopwatch.GetTimestamp());
        var dueMilliseconds = Math.Min(
            int.MaxValue,
            Math.Max(1, StopwatchTicksToMilliseconds(remainingTicks)));
        _timeoutTimer.Change(dueMilliseconds, Timeout.Infinite);
    }

    private static long MillisecondsToStopwatchTicks(long milliseconds) =>
        checked(milliseconds * Stopwatch.Frequency / 1000);

    private static long StopwatchTicksToMilliseconds(long ticks) =>
        ticks * 1000 / Stopwatch.Frequency;

    private bool TryRemove(int messageId, PendingResponse pending)
    {
        lock (_requestsGate)
        {
            return TryRemoveCore(messageId, pending);
        }
    }

    private bool TryRemoveCore(int messageId, PendingResponse pending)
    {
        if (!_requests.TryGetValue(messageId, out var current) ||
            !ReferenceEquals(current, pending))
        {
            return false;
        }

        _requests.Remove(messageId);
        return true;
    }
}
