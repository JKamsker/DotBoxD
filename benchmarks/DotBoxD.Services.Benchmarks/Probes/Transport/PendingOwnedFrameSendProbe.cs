using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingOwnedFrameSendProbe
{
    private const int WarmupIterations = 20_000;
    private const int Iterations = 1_000_000;
    private static readonly byte[] s_rawFrame = CreateRawFrame();
    private static readonly long s_frameChecksum = CalculateChecksum(s_rawFrame);

    public static void Run()
    {
        using var synchronousStream = new ControlledWriteStream(forcePending: false);
        using var pendingStream = new ControlledWriteStream(forcePending: true);
        var synchronousConnection = new StreamConnection(synchronousStream, ownsStream: false);
        var pendingConnection = new StreamConnection(pendingStream, ownsStream: false);
        using var synchronousPeerSender = new RpcPeerSender(synchronousConnection, static () => false);
        using var pendingPeerSender = new RpcPeerSender(pendingConnection, static () => false);
        var synchronousStreamSender = CreateStreamSender(synchronousConnection);
        var pendingStreamSender = CreateStreamSender(pendingConnection);
        var synchronousPeerStreamSender = CreatePeerStreamSender(synchronousPeerSender);
        var pendingPeerStreamSender = CreatePeerStreamSender(pendingPeerSender);
        var meter = new FrameSendProbeMeter(
            WarmupIterations,
            Iterations,
            s_rawFrame.Length,
            s_frameChecksum);
        try
        {
            VerifyOwnedFrameLifetime(synchronousConnection, synchronousStream);
            VerifyOwnedFrameLifetime(pendingConnection, pendingStream);
            var synchronousRaw = meter.Measure(
                "synchronous raw send",
                synchronousStream,
                () => SendRaw(synchronousConnection, synchronousStream));
            var synchronousOwned = meter.Measure(
                "synchronous owned-frame send",
                synchronousStream,
                () => SendOwnedFrame(synchronousConnection, synchronousStream));
            var synchronousStreaming = meter.Measure(
                "synchronous streaming-frame send",
                synchronousStream,
                () => SendStreamingFrame(synchronousStreamSender, synchronousStream));
            var synchronousPeerOwned = meter.Measure(
                "synchronous peer owned-frame send",
                synchronousStream,
                () => SendOwnedFrame(synchronousPeerSender, synchronousStream));
            var synchronousPeerStreaming = meter.Measure(
                "synchronous peer streaming-frame send",
                synchronousStream,
                () => SendStreamingFrame(synchronousPeerStreamSender, synchronousStream));
            var pendingRaw = meter.Measure(
                "forced-pending raw send",
                pendingStream,
                () => SendRaw(pendingConnection, pendingStream));
            var pendingOwned = meter.Measure(
                "forced-pending owned-frame send",
                pendingStream,
                () => SendOwnedFrame(pendingConnection, pendingStream));
            var pendingStreaming = meter.Measure(
                "forced-pending streaming-frame send",
                pendingStream,
                () => SendStreamingFrame(pendingStreamSender, pendingStream));
            var pendingPeerOwned = meter.Measure(
                "forced-pending peer owned-frame send",
                pendingStream,
                () => SendOwnedFrame(pendingPeerSender, pendingStream));
            var pendingPeerStreaming = meter.Measure(
                "forced-pending peer streaming-frame send",
                pendingStream,
                () => SendStreamingFrame(pendingPeerStreamSender, pendingStream));

            Console.WriteLine("Pending owned-frame send probe");
            Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
            Console.WriteLine(
                $"verified frame bytes = {s_rawFrame.Length}; " +
                $"per-frame checksum = {s_frameChecksum}");
            FrameSendProbeMeter.Write(synchronousRaw);
            FrameSendProbeMeter.Write(synchronousOwned);
            FrameSendProbeMeter.Write(synchronousStreaming);
            FrameSendProbeMeter.Write(synchronousPeerOwned);
            FrameSendProbeMeter.Write(synchronousPeerStreaming);
            FrameSendProbeMeter.Write(pendingRaw);
            FrameSendProbeMeter.Write(pendingOwned);
            FrameSendProbeMeter.Write(pendingStreaming);
            FrameSendProbeMeter.Write(pendingPeerOwned);
            FrameSendProbeMeter.Write(pendingPeerStreaming);
            var synchronousOwnershipCost =
                synchronousOwned.NanosecondsPerOperation - synchronousRaw.NanosecondsPerOperation;
            var pendingOwnershipCost =
                pendingOwned.NanosecondsPerOperation - pendingRaw.NanosecondsPerOperation;
            Console.WriteLine(
                $"pending ownership overhead       " +
                $"{pendingOwnershipCost,9:N1} ns/op " +
                $"{pendingOwned.BytesPerOperation - pendingRaw.BytesPerOperation,9:N1} B/op");
            Console.WriteLine(
                $"pending state-machine overhead   " +
                $"{pendingOwnershipCost - synchronousOwnershipCost,9:N1} ns/op");
            var synchronousStreamingCost =
                synchronousStreaming.NanosecondsPerOperation - synchronousOwned.NanosecondsPerOperation;
            var pendingStreamingCost =
                pendingStreaming.NanosecondsPerOperation - pendingOwned.NanosecondsPerOperation;
            Console.WriteLine(
                $"pending streaming overhead       " +
                $"{pendingStreamingCost,9:N1} ns/op " +
                $"{pendingStreaming.BytesPerOperation - pendingOwned.BytesPerOperation,9:N1} B/op");
            Console.WriteLine(
                $"streaming state-machine overhead " +
                $"{pendingStreamingCost - synchronousStreamingCost,9:N1} ns/op");
            WritePeerStreamingOverhead(
                "synchronous peer streaming overhead",
                synchronousPeerOwned,
                synchronousPeerStreaming);
            WritePeerStreamingOverhead(
                "pending peer streaming overhead",
                pendingPeerOwned,
                pendingPeerStreaming);
        }
        finally
        {
            synchronousConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            pendingConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void SendRaw(StreamConnection connection, ControlledWriteStream stream)
    {
        var pending = connection.SendValueAsync(s_rawFrame);
        CompleteSend(pending, stream);
    }

    private static void SendOwnedFrame(StreamConnection connection, ControlledWriteStream stream)
    {
        var frame = CreateOwnedFrame();
        var pending = connection.SendFrameValueAsync(frame);
        CompleteSend(pending, stream);
    }

    private static void SendOwnedFrame(RpcPeerSender sender, ControlledWriteStream stream)
    {
        var frame = CreateOwnedFrame();
        var pending = sender.SendFrameValueAsync(frame, CancellationToken.None);
        CompleteSend(pending, stream);
    }

    private static void SendStreamingFrame(
        RpcStreamFrameSender sender,
        ControlledWriteStream stream)
    {
        var frame = CreateOwnedFrame();
        var pending = sender.SendAsync(frame, CancellationToken.None);
        CompleteSend(pending, stream);
    }

    private static RpcStreamFrameSender CreateStreamSender(StreamConnection connection) =>
        new(connection.SendAsync, connection.SendFrameValueAsync);

    private static RpcStreamFrameSender CreatePeerStreamSender(RpcPeerSender sender) =>
        new(
            sender.SendAsync,
            sender.ValidatedFrameSender ??
                throw new InvalidOperationException("Expected a validated built-in frame sender."));

    private static void CompleteSend(ValueTask pending, ControlledWriteStream stream)
    {
        if (!stream.ForcePending)
        {
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("The synchronous write did not complete synchronously.");
            }

            pending.GetAwaiter().GetResult();
            return;
        }

        if (pending.IsCompleted)
        {
            throw new InvalidOperationException("The controlled write did not remain pending.");
        }

        stream.CompletePendingWrite();
        pending.GetAwaiter().GetResult();
        stream.ResetCompletedWrite();
    }

    private static void VerifyOwnedFrameLifetime(
        StreamConnection connection,
        ControlledWriteStream stream)
    {
        var frame = CreateOwnedFrame();
        var pending = connection.SendFrameValueAsync(frame);
        if (!stream.ForcePending)
        {
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("The lifetime check did not complete synchronously.");
            }

            pending.GetAwaiter().GetResult();
        }
        else
        {
            if (pending.IsCompleted)
            {
                throw new InvalidOperationException("The lifetime check did not remain pending.");
            }

            _ = frame.WrittenMemory;
            stream.CompletePendingWrite();
            pending.GetAwaiter().GetResult();
            stream.ResetCompletedWrite();
        }

        try
        {
            _ = frame.WrittenMemory;
            throw new InvalidOperationException("The completed send did not release its frame.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static PooledBufferWriter CreateOwnedFrame()
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static byte[] CreateRawFrame()
    {
        using var frame = CreateOwnedFrame();
        return frame.WrittenMemory.ToArray();
    }

    private static long CalculateChecksum(ReadOnlySpan<byte> frame)
    {
        var checksum = 0L;
        for (var index = 0; index < frame.Length; index++)
        {
            checksum += (index + 1L) * frame[index];
        }

        return checksum;
    }

    private static void WritePeerStreamingOverhead(
        string name,
        FrameSendMeasurement owned,
        FrameSendMeasurement streaming) =>
        Console.WriteLine(
            $"{name,-40} " +
            $"{streaming.NanosecondsPerOperation - owned.NanosecondsPerOperation,9:N1} ns/op " +
            $"{streaming.BytesPerOperation - owned.BytesPerOperation,9:N1} B/op");
}
