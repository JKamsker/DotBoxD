using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Diagnostics;

/// <summary>
/// Converts sandbox audit events into standard .NET activities and metrics without selecting an exporter.
/// Pass <see cref="Observe"/> to <c>SandboxHostBuilder.ForwardAuditEventsTo</c>.
/// </summary>
public static class SandboxTelemetry
{
    public const string InstrumentationName = "DotBoxD.Hosting";

    public static ActivitySource Activities { get; } = new(InstrumentationName);

    public static Meter Metrics { get; } = new(InstrumentationName);

    private static readonly Counter<long> s_events = Metrics.CreateCounter<long>(
        "dotboxd.sandbox.audit.events",
        description: "Sandbox audit events by kind and outcome.");

    private static readonly Counter<long> s_denials = Metrics.CreateCounter<long>(
        "dotboxd.sandbox.capability.denials",
        description: "Capability and policy denials.");

    private static readonly Counter<long> s_fallbacks = Metrics.CreateCounter<long>(
        "dotboxd.sandbox.execution.fallbacks",
        description: "Compiled-to-interpreted execution fallbacks.");

    private static readonly Histogram<long> s_fuel = Metrics.CreateHistogram<long>(
        "dotboxd.sandbox.fuel.used",
        description: "Fuel consumed by completed sandbox runs.");

    private static readonly Counter<long> s_runs = Metrics.CreateCounter<long>(
        "dotboxd.sandbox.runs",
        description: "Completed sandbox runs by execution mode and outcome.");

    private static readonly Counter<long> s_timeouts = Metrics.CreateCounter<long>(
        "dotboxd.sandbox.timeouts",
        description: "Sandbox runs terminated by a wall-time deadline.");

    private static readonly Histogram<long> s_loopIterations = ResourceHistogram(
        "dotboxd.sandbox.loop.iterations", "Loop iterations consumed by completed sandbox runs.");
    private static readonly Histogram<long> s_allocatedBytes = ResourceHistogram(
        "dotboxd.sandbox.memory.allocated", "bytes", "Allocation budget consumed by completed sandbox runs.");
    private static readonly Histogram<long> s_hostCalls = ResourceHistogram(
        "dotboxd.sandbox.host.calls", "Host calls consumed by completed sandbox runs.");
    private static readonly Histogram<long> s_fileBytesRead = ResourceHistogram(
        "dotboxd.sandbox.file.read", "bytes", "File bytes read by completed sandbox runs.");
    private static readonly Histogram<long> s_fileBytesWritten = ResourceHistogram(
        "dotboxd.sandbox.file.written", "bytes", "File bytes written by completed sandbox runs.");
    private static readonly Histogram<long> s_networkBytesRead = ResourceHistogram(
        "dotboxd.sandbox.network.read", "bytes", "Network bytes read by completed sandbox runs.");
    private static readonly Histogram<long> s_networkBytesWritten = ResourceHistogram(
        "dotboxd.sandbox.network.written", "bytes", "Network bytes written by completed sandbox runs.");
    private static readonly Histogram<long> s_logEvents = ResourceHistogram(
        "dotboxd.sandbox.log.events", "Log events emitted by completed sandbox runs.");
    private static readonly Histogram<long> s_collectionElements = ResourceHistogram(
        "dotboxd.sandbox.collection.elements", "Collection elements materialized by completed sandbox runs.");
    private static readonly Histogram<long> s_stringBytes = ResourceHistogram(
        "dotboxd.sandbox.string.bytes", "bytes", "String bytes materialized by completed sandbox runs.");

    public static void Observe(SandboxAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var tags = new TagList
        {
            { "dotboxd.audit.kind", auditEvent.Kind },
            { "dotboxd.audit.success", auditEvent.Success },
            { "dotboxd.capability", auditEvent.CapabilityId },
            { "error.type", auditEvent.ErrorCode?.ToString() }
        };
        s_events.Add(1, tags);

        if (auditEvent.Kind is "CapabilityDenied" or "PolicyDenied")
        {
            s_denials.Add(1, tags);
        }

        if (auditEvent.Kind == "ExecutionFallback")
        {
            s_fallbacks.Add(1, tags);
        }

        if (auditEvent.Kind == "RunSummary" && auditEvent.Fields is not null)
        {
            RecordRunSummary(auditEvent, tags);
        }

        using var activity = Activities.StartActivity("dotboxd.sandbox.audit", ActivityKind.Internal);
        activity?.SetTag("dotboxd.run.id", auditEvent.RunId.ToString());
        activity?.SetTag("dotboxd.audit.kind", auditEvent.Kind);
        activity?.SetTag("dotboxd.capability", auditEvent.CapabilityId);
        if (!auditEvent.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, auditEvent.Message);
        }
    }

    private static void RecordRunSummary(SandboxAuditEvent auditEvent, TagList tags)
    {
        var fields = auditEvent.Fields!;
        fields.TryGetValue("executionMode", out var executionMode);
        tags.Add("dotboxd.execution.mode", executionMode ?? "unknown");
        s_runs.Add(1, tags);

        if (auditEvent.ErrorCode == SandboxErrorCode.Timeout)
        {
            s_timeouts.Add(1, tags);
        }

        Record(fields, "fuelUsed", s_fuel, tags);
        Record(fields, "loopIterations", s_loopIterations, tags);
        Record(fields, "allocatedBytes", s_allocatedBytes, tags);
        Record(fields, "hostCalls", s_hostCalls, tags);
        Record(fields, "fileBytesRead", s_fileBytesRead, tags);
        Record(fields, "fileBytesWritten", s_fileBytesWritten, tags);
        Record(fields, "networkBytesRead", s_networkBytesRead, tags);
        Record(fields, "networkBytesWritten", s_networkBytesWritten, tags);
        Record(fields, "logEvents", s_logEvents, tags);
        Record(fields, "collectionElements", s_collectionElements, tags);
        Record(fields, "stringBytes", s_stringBytes, tags);
    }

    private static void Record(
        IReadOnlyDictionary<string, string> fields,
        string field,
        Histogram<long> histogram,
        TagList tags)
    {
        if (fields.TryGetValue(field, out var text) &&
            long.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            histogram.Record(value, tags);
        }
    }

    private static Histogram<long> ResourceHistogram(string name, string description)
        => Metrics.CreateHistogram<long>(name, description: description);

    private static Histogram<long> ResourceHistogram(string name, string unit, string description)
        => Metrics.CreateHistogram<long>(name, unit, description);
}
