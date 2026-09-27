namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerFailureObservationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public Task Late_failure_is_observed_after_the_host_stops_waiting(
        bool timeout, bool disposeBeforeFailure, bool customSource)
        => AssertObservedAsync(marker => WorkerCancellationFixture.AbandonFailure(
            timeout, disposeBeforeFailure, customSource, marker));

    internal static async Task AssertObservedAsync(Func<string, WorkerCancellationFixture.Probe> create)
    {
        var marker = Guid.NewGuid().ToString("N");
        var notifications = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception.InnerExceptions.Any(error => string.Equals(error.Message, marker, StringComparison.Ordinal)))
            {
                Interlocked.Increment(ref notifications);
                args.SetObserved();
            }
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            var probe = create(marker);
            var deadline = Environment.TickCount64 + 5_000;
            do
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (!probe.Completed.IsAlive)
                {
                    break;
                }
                await Task.Delay(10);
            } while (Environment.TickCount64 < deadline);

            Assert.False(probe.Completed.IsAlive, "Completed work must be collectible before checking notifications.");
            Assert.Equal(probe.ExpectedConsumptions, Volatile.Read(ref probe.Counter.Calls));
            Assert.Equal(0, Volatile.Read(ref notifications));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }
}
