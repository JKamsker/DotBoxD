using System.Diagnostics;
using System.Diagnostics.Metrics;
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

    internal static RpcServerRequestScope StartServerRequest(RpcRequest request)
        => new(request, s_serverDuration);

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
}

internal sealed class RpcServerRequestScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly Histogram<double> _duration;
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly string _method;
    private readonly string _service;
    private bool _failed;

    public RpcServerRequestScope(RpcRequest request, Histogram<double> duration)
    {
        _duration = duration;
        _service = request.ServiceName ?? string.Empty;
        _method = request.MethodName ?? string.Empty;
        _activity = RpcTelemetry.Activities.StartActivity("dotboxd.rpc.server", ActivityKind.Server);
        _activity?.SetTag("rpc.system", "dotboxd");
        _activity?.SetTag("rpc.service", _service);
        _activity?.SetTag("rpc.method", _method);
    }

    public void MarkFailed(Exception error)
    {
        _failed = true;
        _activity?.SetTag("error.type", error.GetType().FullName);
        _activity?.SetStatus(ActivityStatusCode.Error, error.Message);
    }

    public void Dispose()
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
        _activity?.Dispose();
    }
}
