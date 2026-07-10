using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Diagnostics;

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
}
