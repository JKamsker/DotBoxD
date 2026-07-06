using System.Diagnostics;

namespace DotBoxD.Services.Client;

internal sealed partial class PendingRequests
{
    private void ScheduleTimeout(long deadline)
    {
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
        if (!BeginTimeoutScan())
        {
            return;
        }

        var scan = ScanExpiredRequests(Stopwatch.GetTimestamp());
        CancelExpiredResponses(scan.Expired);
        RescheduleTimeout(scan.Next);
    }

    private bool BeginTimeoutScan()
    {
        lock (_timeoutGate)
        {
            if (_disposed != 0)
            {
                return false;
            }

            _nextTimeoutTimestamp = long.MaxValue;
            return true;
        }
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

    private void RescheduleTimeout(long next)
    {
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
        if (_disposed != 0)
        {
            return;
        }

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

    private readonly record struct TimeoutScan(List<IPendingResponse>? Expired, long Next);
}
