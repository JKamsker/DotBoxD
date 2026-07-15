using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Diagnostics;

[Collection(RpcTelemetryCollection.Name)]
public sealed class RpcTelemetryTests
{
    [Fact]
    public void Operational_failures_emit_standard_counters()
    {
        var measurements = new ConcurrentBag<string>();
        using var listener = ListenForLongMeasurements(RpcTelemetry.InstrumentationName, measurements);

        RpcTelemetry.ProtocolFrameRejected(MessageType.Request, serializationFailure: true);
        RpcTelemetry.QueueSaturated();
        RpcTelemetry.RequestTimedOut();

        Assert.Contains("dotboxd.rpc.frames.rejected", measurements);
        Assert.Contains("dotboxd.rpc.serialization.failures", measurements);
        Assert.Contains("dotboxd.rpc.queue.saturation", measurements);
        Assert.Contains("dotboxd.rpc.requests.timeouts", measurements);
    }

    [Fact]
    public void Diagnostic_errors_emit_activity_details_when_listened_to()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = ListenForActivities(RpcTelemetry.InstrumentationName, stopped);
        var error = new InvalidOperationException("boom");

        RpcDiagnostics.Report("telemetry-test", error);

        var activity = Assert.Single(stopped);
        Assert.Equal("dotboxd.rpc.diagnostic.error", activity.OperationName);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("telemetry-test", ReadTag(activity, "rpc.operation"));
        Assert.Equal(error.GetType().FullName, ReadTag(activity, "error.type"));
    }

    [Fact]
    public void Server_request_scope_emits_failure_activity_and_timeout_counter()
    {
        var measurements = new ConcurrentBag<string>();
        var stopped = new ConcurrentBag<Activity>();
        using var meterListener = ListenForLongMeasurements(RpcTelemetry.InstrumentationName, measurements);
        using var activityListener = ListenForActivities(RpcTelemetry.InstrumentationName, stopped);

        using (var scope = RpcTelemetry.StartServerRequest())
        {
            scope.SetResolvedOperation("GameService", "Move");
            scope.MarkFailed(new ServiceTimeoutException("timed out"));
        }

        Assert.Contains("dotboxd.rpc.requests.timeouts", measurements);
        var activity = Assert.Single(stopped);
        Assert.Equal("dotboxd.rpc.server", activity.OperationName);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("GameService", ReadTag(activity, "rpc.service"));
        Assert.Equal("Move", ReadTag(activity, "rpc.method"));
        Assert.Equal(typeof(ServiceTimeoutException).FullName, ReadTag(activity, "error.type"));
    }

    private static MeterListener ListenForLongMeasurements(
        string instrumentationName,
        ConcurrentBag<string> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == instrumentationName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();
        return listener;
    }

    private static ActivityListener ListenForActivities(
        string instrumentationName,
        ConcurrentBag<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == instrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static object? ReadTag(Activity activity, string key)
    {
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key == key)
            {
                return tag.Value;
            }
        }

        return null;
    }
}
