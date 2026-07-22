using DotBoxD.Services.Client;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PendingRequestTimeoutSchedulerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Later_deadline_scheduled_during_timeout_scan_is_not_lost()
    {
        using var firstScanStarted = new ManualResetEventSlim();
        using var releaseFirstScan = new ManualResetEventSlim();
        using var secondTimeoutFired = new ManualResetEventSlim();
        PendingRequestTimeoutScheduler scheduler = null!;
        Exception? callbackError = null;
        var callbacks = 0;
        scheduler = new PendingRequestTimeoutScheduler(() =>
        {
            try
            {
                if (Interlocked.Increment(ref callbacks) == 1)
                {
                    firstScanStarted.Set();
                    releaseFirstScan.Wait(TestTimeout);
                    scheduler.Reschedule(long.MaxValue);
                    return;
                }

                secondTimeoutFired.Set();
            }
            catch (Exception exception)
            {
                callbackError = exception;
                secondTimeoutFired.Set();
            }
        });

        using (scheduler)
        {
            scheduler.Schedule(PendingRequestTimeoutScheduler.GetDeadline(TimeSpan.FromMilliseconds(20)));
            Assert.True(firstScanStarted.Wait(TestTimeout));
            scheduler.Schedule(PendingRequestTimeoutScheduler.GetDeadline(TimeSpan.FromMilliseconds(200)));
            releaseFirstScan.Set();

            Assert.True(secondTimeoutFired.Wait(TestTimeout));
            Assert.Null(callbackError);
            Assert.Equal(2, Volatile.Read(ref callbacks));
        }
    }
}
