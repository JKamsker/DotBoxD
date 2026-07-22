namespace DotBoxD.LookaheadCapacityProbe;

internal enum ProbeScenario
{
    Gated,
    Burst,
    Fragmented,
}

internal enum ProbeTransport
{
    NamedPipe,
    Tcp,
}

internal readonly record struct ProbeMeasurement(
    ProbeTransport Transport,
    ProbeScenario Scenario,
    int FrameLength,
    int Capacity,
    int FrameCount,
    int BatchCount,
    long ElapsedTicks,
    long AllocatedBytes,
    long ReadCount,
    long PendingReadCount,
    int PendingReceiveCount,
    long Checksum)
{
    public double AllocatedBytesPerFrame => AllocatedBytes / (double)FrameCount;

    public double NanosecondsPerFrame =>
        ElapsedTicks * CapacityProbe.NanosecondsPerTick / FrameCount;

    public double PendingReadsPerFrame => PendingReadCount / (double)FrameCount;

    public double PendingReceivePercent => PendingReceiveCount * 100d / FrameCount;

    public double ReadsPerFrame => ReadCount / (double)FrameCount;
}

internal sealed record ProbeOptions(
    bool Quick,
    int Scale,
    ProbeTransport[] Transports,
    ProbeScenario[] Scenarios,
    int[] FrameLengths,
    int[] Capacities)
{
    public static ProbeOptions Parse(string[] args, int[] defaultFrames, int[] defaultCapacities) =>
        new(
            args.Contains("--quick", StringComparer.Ordinal),
            SelectScale(args),
            SelectTransport(args),
            SelectScenario(args),
            SelectValues(args, "--frame=", defaultFrames),
            SelectValues(args, "--capacity=", defaultCapacities));

    private static int SelectScale(string[] args)
    {
        var values = SelectValues(args, "--scale=", [1]);
        if (values.Length != 1 || values[0] < 1)
        {
            throw new ArgumentException("--scale requires one positive integer.", nameof(args));
        }

        return values[0];
    }

    private static ProbeTransport[] SelectTransport(string[] args)
    {
        if (args.Contains("--named-pipe", StringComparer.Ordinal))
        {
            return [ProbeTransport.NamedPipe];
        }

        if (args.Contains("--tcp", StringComparer.Ordinal))
        {
            return [ProbeTransport.Tcp];
        }

        return [ProbeTransport.NamedPipe, ProbeTransport.Tcp];
    }

    private static ProbeScenario[] SelectScenario(string[] args)
    {
        if (args.Contains("--gated", StringComparer.Ordinal))
        {
            return [ProbeScenario.Gated];
        }

        if (args.Contains("--burst", StringComparer.Ordinal))
        {
            return [ProbeScenario.Burst];
        }

        if (args.Contains("--fragmented", StringComparer.Ordinal))
        {
            return [ProbeScenario.Fragmented];
        }

        return [ProbeScenario.Gated, ProbeScenario.Burst, ProbeScenario.Fragmented];
    }

    private static int[] SelectValues(string[] args, string prefix, int[] defaults)
    {
        var argument = Array.Find(args, value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (argument is null)
        {
            return defaults;
        }

        var values = argument[prefix.Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException($"{prefix} requires at least one integer value.", nameof(args));
        }

        return values;
    }
}
