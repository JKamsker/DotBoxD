using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class SendProbeFrame
{
    public static readonly byte[] Raw = CreateRaw();
    public static readonly long Checksum = CalculateChecksum(Raw);

    public static int Length => Raw.Length;

    public static PooledBufferWriter Rent()
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    public static SendProbeFrameLease CaptureLease(PooledBufferWriter frame) =>
        new(frame, frame.LeaseToken);

    public static void AssertDisposed(SendProbeFrameLease lease)
    {
        try
        {
            _ = lease.Frame.GetWrittenMemory(lease.Token);
            throw new InvalidOperationException("A completed send did not dispose its owned lease.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public static long CalculateChecksum(ReadOnlySpan<byte> frame)
    {
        var checksum = 0L;
        for (var index = 0; index < frame.Length; index++)
        {
            checksum += (index + 1L) * frame[index];
        }

        return checksum;
    }

    private static byte[] CreateRaw()
    {
        using var frame = Rent();
        return frame.WrittenMemory.ToArray();
    }
}

internal readonly record struct SendProbeFrameLease(PooledBufferWriter Frame, long Token);
