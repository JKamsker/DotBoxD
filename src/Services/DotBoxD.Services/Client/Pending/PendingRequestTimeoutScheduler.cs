using System.Diagnostics;

namespace DotBoxD.Services.Client;

internal sealed class PendingRequestTimeoutScheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly Action _cancelExpired;
    private readonly Timer _timer;
    private long _nextTimeoutTimestamp = long.MaxValue;
    private int _disposed;

    public PendingRequestTimeoutScheduler(Action cancelExpired)
    {
        _cancelExpired = cancelExpired;
        _timer = new Timer(
            static state => ((PendingRequestTimeoutScheduler)state!).CancelExpired(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public static long GetDeadline(TimeSpan timeout)
    {
        var timeoutTicks = MillisecondsToStopwatchTicks((long)Math.Ceiling(timeout.TotalMilliseconds));
        return Stopwatch.GetTimestamp() + timeoutTicks;
    }

    public void Schedule(long deadline)
    {
        if (Volatile.Read(ref _disposed) != 0 || deadline >= Volatile.Read(ref _nextTimeoutTimestamp))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed != 0 || deadline >= _nextTimeoutTimestamp)
            {
                return;
            }

            Volatile.Write(ref _nextTimeoutTimestamp, deadline);
            ScheduleTimerLocked();
        }
    }

    public void Reschedule(long next)
    {
        lock (_gate)
        {
            if (_disposed != 0)
            {
                return;
            }

            if (next < _nextTimeoutTimestamp)
            {
                Volatile.Write(ref _nextTimeoutTimestamp, next);
            }

            ScheduleTimerLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
            _timer.Dispose();
        }
    }

    private void CancelExpired()
    {
        if (!BeginTimeoutScan())
        {
            return;
        }

        _cancelExpired();
    }

    private bool BeginTimeoutScan()
    {
        lock (_gate)
        {
            if (_disposed != 0)
            {
                return false;
            }

            Volatile.Write(ref _nextTimeoutTimestamp, long.MaxValue);
            return true;
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
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var remainingTicks = Math.Max(0, _nextTimeoutTimestamp - Stopwatch.GetTimestamp());
        var dueMilliseconds = Math.Min(
            int.MaxValue,
            Math.Max(1, StopwatchTicksToMilliseconds(remainingTicks)));
        _timer.Change(dueMilliseconds, Timeout.Infinite);
    }

    private static long MillisecondsToStopwatchTicks(long milliseconds) =>
        checked(milliseconds * Stopwatch.Frequency / 1000);

    private static long StopwatchTicksToMilliseconds(long ticks) =>
        ticks * 1000 / Stopwatch.Frequency;
}
