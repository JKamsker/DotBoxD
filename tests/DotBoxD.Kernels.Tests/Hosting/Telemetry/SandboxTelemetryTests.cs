using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DotBoxD.Hosting.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting.Telemetry;

public sealed class SandboxTelemetryTests
{
    [Fact]
    public void Run_summary_emits_mode_timeout_and_resource_measurements()
    {
        var measurements = new ConcurrentBag<string>();
        using var listener = ListenForLongMeasurements(measurements);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["executionMode"] = "Interpreted",
            ["fuelUsed"] = "10",
            ["loopIterations"] = "2",
            ["allocatedBytes"] = "30",
            ["hostCalls"] = "1",
            ["fileBytesRead"] = "4",
            ["fileBytesWritten"] = "5",
            ["networkBytesRead"] = "6",
            ["networkBytesWritten"] = "7",
            ["logEvents"] = "8",
            ["collectionElements"] = "9",
            ["stringBytes"] = "11"
        };
        var auditEvent = new SandboxAuditEvent(
            new SandboxRunId(Guid.NewGuid()),
            "RunSummary",
            DateTimeOffset.UtcNow,
            Success: false,
            ErrorCode: SandboxErrorCode.Timeout,
            Fields: fields);

        SandboxTelemetry.Observe(auditEvent);

        Assert.Contains("dotboxd.sandbox.runs", measurements);
        Assert.Contains("dotboxd.sandbox.timeouts", measurements);
        Assert.Contains("dotboxd.sandbox.fuel.used", measurements);
        Assert.Contains("dotboxd.sandbox.loop.iterations", measurements);
        Assert.Contains("dotboxd.sandbox.memory.allocated", measurements);
        Assert.Contains("dotboxd.sandbox.host.calls", measurements);
        Assert.Contains("dotboxd.sandbox.file.read", measurements);
        Assert.Contains("dotboxd.sandbox.file.written", measurements);
        Assert.Contains("dotboxd.sandbox.network.read", measurements);
        Assert.Contains("dotboxd.sandbox.network.written", measurements);
        Assert.Contains("dotboxd.sandbox.log.events", measurements);
        Assert.Contains("dotboxd.sandbox.collection.elements", measurements);
        Assert.Contains("dotboxd.sandbox.string.bytes", measurements);
    }

    private static MeterListener ListenForLongMeasurements(ConcurrentBag<string> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == SandboxTelemetry.InstrumentationName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();
        return listener;
    }
}
