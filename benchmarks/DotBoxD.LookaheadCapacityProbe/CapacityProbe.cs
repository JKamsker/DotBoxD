using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using DotBoxD.Services.Buffers;

namespace DotBoxD.LookaheadCapacityProbe;

internal static class CapacityProbe
{
    private static readonly int[] s_capacities = [4, 64, 256, 1_024, 4_096, 16_384, 65_536];
    private static readonly int[] s_frameLengths = [32, 256, 1_024, 16_384, 262_144];

    internal static readonly double NanosecondsPerTick = 1_000_000_000d / Stopwatch.Frequency;

    public static async Task RunAsync(string[] args)
    {
        var options = ProbeOptions.Parse(args, s_frameLengths, s_capacities);

        Console.WriteLine(
            "transport\tscenario\tframe_B\tcapacity_B\tframes\tbatches\tns/frame\tB/frame\t" +
            "reads/frame\tpending_reads/frame\tpending_receive_%\tchecksum");
        foreach (var transport in options.Transports)
        {
            foreach (var scenario in options.Scenarios)
            {
                foreach (var frameLength in options.FrameLengths)
                {
                    foreach (var capacity in options.Capacities)
                    {
                        var measurement = await MeasureAsync(
                            transport,
                            scenario,
                            frameLength,
                            capacity,
                            options.Quick,
                            options.Scale).ConfigureAwait(false);
                        Write(measurement);
                    }
                }
            }
        }
    }

    private static async Task<ProbeMeasurement> MeasureAsync(
        ProbeTransport transport,
        ProbeScenario scenario,
        int frameLength,
        int capacity,
        bool quick,
        int scale)
    {
        var frame = CreateFrame(frameLength);
        var framesPerBatch = scenario == ProbeScenario.Burst
            ? Math.Clamp(65_536 / frameLength, 4, 256)
            : 1;
        var frameCount = GetFrameCount(scenario, frameLength, framesPerBatch, quick) * scale;
        var batchCount = frameCount / framesPerBatch;
        var writeBuffer = CreateWriteBuffer(frame, framesPerBatch);
        var fragmentLength = scenario == ProbeScenario.Fragmented
            ? Math.Max(1, (frameLength + 15) / 16)
            : writeBuffer.Length;

        await using var pair = await TransportPair.CreateAsync(transport).ConfigureAwait(false);
        using var reader = new LookaheadFrameReader(pair.Reader, capacity);
        using var writer = new GatedWriter(pair.Writer, writeBuffer, fragmentLength);

        var warmupBatches = Math.Min(batchCount, scenario == ProbeScenario.Burst ? 4 : 32);
        await RunBatchesAsync(
            reader,
            writer,
            frameLength,
            framesPerBatch,
            warmupBatches).ConfigureAwait(false);

        ForceGc();
        var readsBefore = pair.Reader.ReadCount;
        var pendingReadsBefore = pair.Reader.PendingReadCount;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var result = await RunBatchesAsync(
            reader,
            writer,
            frameLength,
            framesPerBatch,
            batchCount).ConfigureAwait(false);
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var readCount = pair.Reader.ReadCount - readsBefore;
        var pendingReadCount = pair.Reader.PendingReadCount - pendingReadsBefore;

        var expectedChecksum = Checksum(frame) * frameCount;
        if (result.Checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"Checksum mismatch: observed {result.Checksum:N0}, expected {expectedChecksum:N0}.");
        }

        return new ProbeMeasurement(
            transport,
            scenario,
            frameLength,
            capacity,
            frameCount,
            batchCount,
            result.ElapsedTicks,
            allocatedBytes,
            readCount,
            pendingReadCount,
            result.PendingReceiveCount,
            result.Checksum);
    }

    private static async Task<BatchResult> RunBatchesAsync(
        LookaheadFrameReader reader,
        GatedWriter writer,
        int frameLength,
        int framesPerBatch,
        int batchCount)
    {
        long checksum = 0;
        long elapsedTicks = 0;
        var pendingReceiveCount = 0;
        for (var batch = 0; batch < batchCount; batch++)
        {
            var first = reader.ReadFrameAsync();
            if (!first.IsCompletedSuccessfully)
            {
                pendingReceiveCount++;
            }

            writer.Release();
            using (var payload = await first.ConfigureAwait(false))
            {
                checksum += ValidateAndChecksum(payload, frameLength);
            }

            for (var frame = 1; frame < framesPerBatch; frame++)
            {
                var pending = reader.ReadFrameAsync();
                if (!pending.IsCompletedSuccessfully)
                {
                    pendingReceiveCount++;
                }

                using var payload = await pending.ConfigureAwait(false);
                checksum += ValidateAndChecksum(payload, frameLength);
            }

            var completedAt = Stopwatch.GetTimestamp();
            var startedAt = writer.WaitForCompletion();
            elapsedTicks += completedAt - startedAt;
        }

        return new BatchResult(checksum, elapsedTicks, pendingReceiveCount);
    }

    private static long ValidateAndChecksum(Payload payload, int frameLength)
    {
        if (payload.Length != frameLength)
        {
            throw new InvalidOperationException(
                $"Received {payload.Length:N0} bytes instead of {frameLength:N0}.");
        }

        return Checksum(payload.Span);
    }

    private static long Checksum(ReadOnlySpan<byte> frame) =>
        BinaryPrimitives.ReadInt32LittleEndian(frame) + frame[4] + frame[frame.Length / 2] + frame[^1];

    private static byte[] CreateFrame(int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        for (var index = sizeof(int); index < frame.Length; index++)
        {
            frame[index] = unchecked((byte)(index * 31));
        }

        return frame;
    }

    private static byte[] CreateWriteBuffer(byte[] frame, int framesPerBatch)
    {
        var buffer = new byte[frame.Length * framesPerBatch];
        for (var frameIndex = 0; frameIndex < framesPerBatch; frameIndex++)
        {
            frame.CopyTo(buffer, frameIndex * frame.Length);
        }

        return buffer;
    }

    private static int GetFrameCount(
        ProbeScenario scenario,
        int frameLength,
        int framesPerBatch,
        bool quick)
    {
        if (scenario == ProbeScenario.Burst)
        {
            var batches = quick ? 4 : frameLength >= 262_144 ? 32 : frameLength >= 16_384 ? 16 : 32;
            return framesPerBatch * batches;
        }

        var frames = frameLength switch
        {
            <= 256 => scenario == ProbeScenario.Gated ? 8_000 : 2_048,
            <= 1_024 => scenario == ProbeScenario.Gated ? 5_000 : 1_024,
            <= 16_384 => scenario == ProbeScenario.Gated ? 1_000 : 128,
            _ => scenario == ProbeScenario.Gated ? 128 : 16,
        };
        return quick ? Math.Max(8, frames / 4) : frames;
    }

    private static void Write(ProbeMeasurement measurement)
    {
        var culture = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Join(
            '\t',
            measurement.Transport,
            measurement.Scenario,
            measurement.FrameLength,
            measurement.Capacity,
            measurement.FrameCount,
            measurement.BatchCount,
            measurement.NanosecondsPerFrame.ToString("F1", culture),
            measurement.AllocatedBytesPerFrame.ToString("F1", culture),
            measurement.ReadsPerFrame.ToString("F4", culture),
            measurement.PendingReadsPerFrame.ToString("F4", culture),
            measurement.PendingReceivePercent.ToString("F2", culture),
            measurement.Checksum));
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct BatchResult(
        long Checksum,
        long ElapsedTicks,
        int PendingReceiveCount);
}
