namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingOwnedFrameStageProbe
{
    private const int StreamWarmupIterations = 5_000;
    private const int StreamIterations = 50_000;
    private const int GateWarmupIterations = 1_000;
    private const int GateIterations = 10_000;
    private const int TcpWarmupIterations = 500;
    private const int TcpIterations = 5_000;

    public static async Task RunAsync()
    {
        using var liveTokenSource = new CancellationTokenSource();
        var measurements = new List<PendingSendStageMeasurement>();
        var tokens = new[]
        {
            (Name: "default", Token: CancellationToken.None),
            (Name: "live", Token: liveTokenSource.Token),
        };

        foreach (var stage in Enum.GetValues<PendingSendStage>())
        {
            foreach (var kind in Enum.GetValues<PendingSendKind>())
            {
                foreach (var token in tokens)
                {
                    measurements.Add(MeasureStream(stage, kind, token.Name, token.Token));
                }
            }
        }

        foreach (var kind in Enum.GetValues<PendingSendKind>())
        {
            foreach (var token in tokens)
            {
                measurements.Add(
                    await MeasureNamedPipeAsync(kind, token.Name, token.Token).ConfigureAwait(false));
                measurements.Add(
                    await MeasureTcpAsync(kind, token.Name, token.Token).ConfigureAwait(false));
            }
        }

        Console.WriteLine("Pending owned-frame stage probe");
        Console.WriteLine(
            "caller allocation covers only Send*ValueAsync invocation; loop/process allocation " +
            "also covers completion work but excludes preallocated harness sources");
        Console.WriteLine(
            "lane                                      call ns/op  caller B/op    loop B/op");
        foreach (var measurement in measurements)
        {
            Console.WriteLine(
                $"{measurement.Name,-41} " +
                $"{measurement.CallNanosecondsPerOperation,10:N1} " +
                $"{measurement.CallerBytesPerOperation,12:N1} " +
                $"{measurement.LoopProcessBytesPerOperation,12:N1}");
        }

        Console.WriteLine(
            $"controlled Stream invariants: {SendProbeFrame.Length} bytes/send; checksum " +
            $"{SendProbeFrame.Checksum}; every call pending at exactly one selected stage; " +
            "exact output, caller-token forwarding, gate restoration, and every owned lease verified");
        Console.WriteLine(
            "real IPC invariants: initial held-gate suspension, exact peer bytes, gate restoration, " +
            "and every owned lease verified; later OS writes may also suspend");
    }

    private static PendingSendStageMeasurement MeasureStream(
        PendingSendStage stage,
        PendingSendKind kind,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var isGate = stage == PendingSendStage.Gate;
        var meter = new PendingSendStageMeter(
            isGate ? GateWarmupIterations : StreamWarmupIterations,
            isGate ? GateIterations : StreamIterations,
            expectedFlushesPerSend: 1);
        using var lane = new StreamPendingSendLane(
            stage,
            kind,
            cancellationToken,
            meter.TotalOperations);
        var measurement = meter.Measure(
            $"stream {stage.ToString().ToLowerInvariant()}/{KindName(kind)}/{tokenName}",
            lane.SendOnce,
            lane.Snapshot);
        lane.VerifyOwnedFramesDisposed();
        return measurement;
    }

    private static async Task<PendingSendStageMeasurement> MeasureNamedPipeAsync(
        PendingSendKind kind,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var meter = new PendingSendStageMeter(
            TcpWarmupIterations,
            TcpIterations,
            expectedFlushesPerSend: 0);
        await using var lane = await NamedPipeHeldGateSendLane
            .CreateAsync(kind, cancellationToken, meter.TotalOperations)
            .ConfigureAwait(false);
        var measurement = meter.Measure(
            $"named pipe gate/{KindName(kind)}/{tokenName}",
            lane.SendOnce,
            lane.Snapshot);
        lane.VerifyOwnedFramesDisposed();
        return measurement;
    }

    private static async Task<PendingSendStageMeasurement> MeasureTcpAsync(
        PendingSendKind kind,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var meter = new PendingSendStageMeter(
            TcpWarmupIterations,
            TcpIterations,
            expectedFlushesPerSend: 0);
        await using var lane = await TcpHeldGateSendLane
            .CreateAsync(kind, cancellationToken, meter.TotalOperations)
            .ConfigureAwait(false);
        var measurement = meter.Measure(
            $"tcp gate/{KindName(kind)}/{tokenName}",
            lane.SendOnce,
            lane.Snapshot);
        lane.VerifyOwnedFramesDisposed();
        return measurement;
    }

    private static string KindName(PendingSendKind kind) =>
        kind == PendingSendKind.Raw ? "raw" : "owned";
}
