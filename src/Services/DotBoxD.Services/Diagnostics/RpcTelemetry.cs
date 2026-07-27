using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Diagnostics;

/// <summary>
/// Standard tracing and metrics emitted by DotBoxD.Services. Exporters are selected by the host.
/// </summary>
public static class RpcTelemetry
{
    public const string InstrumentationName = "DotBoxD.Services";

    public static ActivitySource Activities { get; } = new(InstrumentationName);

    public static Meter Metrics { get; } = new(InstrumentationName);

    private static readonly Histogram<double> s_serverDuration = Metrics.CreateHistogram<double>(
        "dotboxd.rpc.server.duration",
        "ms",
        "Duration of inbound RPC dispatches.");

    private static readonly Counter<long> s_diagnosticErrors = Metrics.CreateCounter<long>(
        "dotboxd.rpc.diagnostic.errors",
        description: "Errors reported on best-effort RPC paths.");

    private static readonly UpDownCounter<long> s_activePeers = Metrics.CreateUpDownCounter<long>(
        "dotboxd.rpc.peers.active",
        description: "Currently active RPC peers.");

    private static readonly Counter<long> s_rejectedFrames = Metrics.CreateCounter<long>(
        "dotboxd.rpc.frames.rejected",
        description: "Malformed or unsupported inbound frames rejected by the protocol boundary.");

    private static readonly Counter<long> s_queueSaturation = Metrics.CreateCounter<long>(
        "dotboxd.rpc.queue.saturation",
        description: "Inbound requests rejected because the bounded dispatch queue was full.");

    private static readonly Counter<long> s_timeouts = Metrics.CreateCounter<long>(
        "dotboxd.rpc.requests.timeouts",
        description: "RPC requests that reached their configured deadline.");

    private static readonly Counter<long> s_serializationFailures = Metrics.CreateCounter<long>(
        "dotboxd.rpc.serialization.failures",
        description: "RPC envelopes or payloads that failed serialization or deserialization.");

    internal static RpcServerRequestScope StartServerRequest()
    {
        var measureDuration = s_serverDuration.Enabled;
        if (!measureDuration && !Activities.HasListeners())
        {
            return default;
        }

        var startedTimestamp = measureDuration ? Stopwatch.GetTimestamp() : 0;
        var activity = Activities.StartActivity("dotboxd.rpc.server", ActivityKind.Server);
        if (!measureDuration && activity is null)
        {
            return default;
        }

        // Like ActivitySource, request telemetry snapshots enabled listeners at request start.
        return new RpcServerRequestScope(
            new RpcServerRequestTelemetry(s_serverDuration, measureDuration, startedTimestamp, activity));
    }

    internal static void ReportDiagnosticError(string operation, Exception error)
    {
        var tags = new TagList
        {
            { "rpc.operation", operation },
            { "error.type", error.GetType().FullName }
        };
        s_diagnosticErrors.Add(1, tags);

        using var activity = Activities.StartActivity("dotboxd.rpc.diagnostic.error", ActivityKind.Internal);
        activity?.SetTag("rpc.operation", operation);
        activity?.SetTag("error.type", error.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error, error.Message);
    }

    internal static void PeerStarted() => s_activePeers.Add(1);

    internal static void PeerStopped() => s_activePeers.Add(-1);

    internal static void ProtocolFrameRejected(MessageType messageType, bool serializationFailure)
    {
        s_rejectedFrames.Add(1, new KeyValuePair<string, object?>("rpc.message.type", messageType.ToString()));
        if (serializationFailure)
        {
            s_serializationFailures.Add(1, new KeyValuePair<string, object?>("rpc.direction", "inbound"));
        }
    }

    internal static void QueueSaturated() => s_queueSaturation.Add(1);

    internal static void RequestTimedOut() => s_timeouts.Add(1);

    internal static void ReportServerFailure(Exception error)
    {
        if (error is ServiceTimeoutException or TimeoutException)
        {
            RequestTimedOut();
        }
    }
}

internal readonly struct RpcServerRequestScope : IDisposable
{
    private readonly RpcServerRequestTelemetry? _telemetry;

    public RpcServerRequestScope(RpcServerRequestTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    public void SetResolvedOperation(string service, string method)
        => _telemetry?.SetResolvedOperation(service, method);

    public void MarkFailed(Exception error)
    {
        RpcTelemetry.ReportServerFailure(error);
        _telemetry?.MarkFailed(error);
    }

    public void Dispose() => _telemetry?.Dispose();
}

internal sealed class RpcServerRequestTelemetry : IDisposable
{
    private readonly Activity? _activity;
    private readonly Histogram<double> _duration;
    private readonly bool _measureDuration;
    private readonly long _startedTimestamp;
    private string _method;
    private string _service;
    private int _disposed;
    private bool _failed;

    public RpcServerRequestTelemetry(
        Histogram<double> duration,
        bool measureDuration,
        long startedTimestamp,
        Activity? activity)
    {
        _duration = duration;
        _measureDuration = measureDuration;
        _startedTimestamp = startedTimestamp;
        _service = "unknown";
        _method = "unknown";
        _activity = activity;
        _activity?.SetTag("rpc.system", "dotboxd");
        _activity?.SetTag("rpc.service", _service);
        _activity?.SetTag("rpc.method", _method);
    }

    public void SetResolvedOperation(string service, string method)
    {
        _service = service;
        _method = method;
        _activity?.SetTag("rpc.service", service);
        _activity?.SetTag("rpc.method", method);
    }

    public void MarkFailed(Exception error)
    {
        _failed = true;
        _activity?.SetTag("error.type", error.GetType().FullName);
        _activity?.SetStatus(ActivityStatusCode.Error, error.Message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_measureDuration && _duration.Enabled)
        {
            var elapsed = (Stopwatch.GetTimestamp() - _startedTimestamp) * 1000d / Stopwatch.Frequency;
            var tags = new TagList
            {
                { "rpc.system", "dotboxd" },
                { "rpc.service", _service },
                { "rpc.method", _method },
                { "error.type", _failed ? "dispatch" : null }
            };
            _duration.Record(elapsed, tags);
        }

        _activity?.Dispose();
    }
}
