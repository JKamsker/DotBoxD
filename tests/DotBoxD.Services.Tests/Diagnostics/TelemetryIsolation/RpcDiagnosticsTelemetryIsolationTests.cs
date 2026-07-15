using System.Diagnostics;
using DotBoxD.Services.Diagnostics;
using Xunit;

namespace DotBoxD.Services.Tests.Diagnostics;

public sealed class RpcDiagnosticsTelemetryIsolationTests
{
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    [Fact]
    public async Task Report_isolates_throwing_activity_listeners_from_diagnostic_subscribers()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var operation = "telemetry-listener-isolation-" + Guid.NewGuid().ToString("N");
            var originalError = new InvalidOperationException("original diagnostic failure");
            var listenerFailure = new InvalidOperationException("activity listener failed");
            RpcDiagnosticErrorEventArgs? observed = null;

            void HealthySubscriber(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (args.Operation == operation && ReferenceEquals(args.Error, originalError))
                {
                    observed = args;
                }
            }

            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == RpcTelemetry.InstrumentationName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => throw listenerFailure
            };
            ActivitySource.AddActivityListener(listener);

            RpcDiagnostics.Error += HealthySubscriber;
            try
            {
                var escaped = Record.Exception(() => RpcDiagnostics.Report(operation, originalError));

                Assert.Null(escaped);
                Assert.NotNull(observed);
                Assert.Equal(operation, observed.Operation);
                Assert.Same(originalError, observed.Error);
            }
            finally
            {
                RpcDiagnostics.Error -= HealthySubscriber;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }
}
