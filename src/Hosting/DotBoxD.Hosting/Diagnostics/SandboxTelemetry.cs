using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotBoxD.Kernels.Bindings;

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

        if (auditEvent.Kind == "RunSummary" &&
            auditEvent.Fields?.TryGetValue("fuelUsed", out var fuelText) == true &&
            long.TryParse(fuelText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var fuel))
        {
            s_fuel.Record(fuel, tags);
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
}
