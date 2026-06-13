namespace SafeIR.Plugins;

using SafeIR;

public sealed record PluginExecutionObservation(
    string Entrypoint,
    ExecutionMode RequestedMode,
    ExecutionMode ActualMode,
    bool Succeeded,
    SandboxErrorCode? ErrorCode,
    SandboxErrorCode? FallbackReason,
    string CacheStatus,
    string? RuntimeForm,
    string? CacheKey,
    string? ArtifactHash,
    string? MaterializationStatus);

internal sealed class PluginExecutionObserver
{
    private readonly object _gate = new();
    private readonly List<PluginExecutionObservation> _observations = [];
    private PluginExecutionObservation? _last;

    public PluginExecutionObservation? Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    public IReadOnlyList<PluginExecutionObservation> Snapshot()
    {
        lock (_gate)
        {
            return _observations.ToArray();
        }
    }

    public void Record(string entrypoint, ExecutionMode requestedMode, SandboxExecutionResult result)
    {
        ExtractMarkers(result.AuditEvents, out var summary, out var fallbackReason);
        var observation = new PluginExecutionObservation(
            entrypoint,
            requestedMode,
            result.ActualMode,
            result.Succeeded,
            result.Error?.Code,
            fallbackReason,
            Field(summary, "cacheStatus") ?? "None",
            Field(summary, "runtimeForm"),
            Field(summary, "cacheKey"),
            result.ArtifactHash ?? Field(summary, "artifactHash"),
            Field(summary, "materializationStatus"));

        lock (_gate)
        {
            _observations.Add(observation);
            _last = observation;
        }
    }

    // Walks the audit-event list a single time to recover the two telemetry markers the
    // observation needs: the last RunSummary's fields and the first ExecutionFallback's
    // error code. This replaces two independent full-list LINQ scans, so extraction stays
    // a single pass over the events even as audit volume grows.
    private static void ExtractMarkers(
        IReadOnlyList<SandboxAuditEvent> auditEvents,
        out IReadOnlyDictionary<string, string>? summary,
        out SandboxErrorCode? fallbackReason)
    {
        summary = null;
        fallbackReason = null;
        var fallbackFound = false;

        for (var i = 0; i < auditEvents.Count; i++)
        {
            var auditEvent = auditEvents[i];
            switch (auditEvent.Kind)
            {
                case "RunSummary":
                    summary = auditEvent.Fields;
                    break;
                case "ExecutionFallback" when !fallbackFound:
                    fallbackReason = auditEvent.ErrorCode;
                    fallbackFound = true;
                    break;
            }
        }
    }

    private static string? Field(IReadOnlyDictionary<string, string>? fields, string key)
        => fields is not null && fields.TryGetValue(key, out var value) ? value : null;
}
