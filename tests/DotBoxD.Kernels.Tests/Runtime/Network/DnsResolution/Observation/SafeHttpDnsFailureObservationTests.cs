namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpDnsFailureObservationTests
{
    public static IEnumerable<object[]> LateFailureCases()
    {
        foreach (var mode in new[] { "caller", "context", "timeout" })
        {
            foreach (var customSource in new[] { false, true })
            {
                yield return [mode, customSource, false];
                yield return [mode, customSource, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(LateFailureCases))]
    public Task Late_resolver_failure_is_observed_after_request_cancellation(
        string mode, bool customSource, bool disposeBeforeCompletion)
        => AssertObservedAsync(marker => DnsFailureObservationFixture.Run(
            mode, customSource, "failure", marker, disposeBeforeCompletion));

    internal static async Task AssertObservedAsync(Func<string, DnsFailureObservationFixture.Probe> create)
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
                if (!probe.Released.IsAlive)
                {
                    break;
                }
                await Task.Delay(10);
            } while (Environment.TickCount64 < deadline);

            Assert.False(probe.Released.IsAlive, "Completed resolution must be collectible before checking notifications.");
            Assert.Equal(1, probe.Counter.ResolveCalls);
            Assert.Equal(probe.ExpectedConsumptions, Volatile.Read(ref probe.Counter.ResultCalls));
            Assert.Equal(0, Volatile.Read(ref notifications));
            Assert.Equal(probe.Events, probe.Audit.Events);
            Assert.Equal(probe.BytesRead, probe.Budget.NetworkBytesRead);
            Assert.Equal(probe.BytesWritten, probe.Budget.NetworkBytesWritten);
            GC.KeepAlive(probe.Request);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }
}
