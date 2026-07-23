using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamingFrameValidationProbe
{
    private const int WarmupIterations = 50_000;
    private const int Iterations = 5_000_000;
    private static readonly long s_frameChecksum = CalculateFrameChecksum();

    public static void Run()
    {
        using var synchronousStream = new ControlledWriteStream(forcePending: false);
        using var pendingStream = new ControlledWriteStream(forcePending: true);
        var synchronousConnection = new StreamConnection(synchronousStream, ownsStream: false);
        var pendingConnection = new StreamConnection(pendingStream, ownsStream: false);
        using var synchronousPeer = new RpcPeerSender(synchronousConnection, static () => false);
        using var pendingPeer = new RpcPeerSender(pendingConnection, static () => false);
        var synchronousUntrusted = CreateUntrustedSender(synchronousConnection);
        var pendingUntrusted = CreateUntrustedSender(pendingConnection);
        var synchronousTrusted = CreateTrustedSender(synchronousPeer);
        var pendingTrusted = CreateTrustedSender(pendingPeer);
        var meter = new FrameSendProbeMeter(
            WarmupIterations,
            Iterations,
            MessageFramer.HeaderSize,
            s_frameChecksum);

        try
        {
            var synchronousOwned = meter.Measure(
                "synchronous owned transport",
                synchronousStream,
                () => SendOwned(synchronousConnection, synchronousStream));
            var synchronousUntrustedStreaming = meter.Measure(
                "synchronous untrusted streaming",
                synchronousStream,
                () => SendStreaming(synchronousUntrusted, synchronousStream));
            var synchronousPeerOwned = meter.Measure(
                "synchronous peer owned",
                synchronousStream,
                () => SendOwned(synchronousPeer, synchronousStream));
            var synchronousTrustedStreaming = meter.Measure(
                "synchronous trusted streaming",
                synchronousStream,
                () => SendStreaming(synchronousTrusted, synchronousStream));
            var pendingOwned = meter.Measure(
                "forced-pending owned transport",
                pendingStream,
                () => SendOwned(pendingConnection, pendingStream));
            var pendingUntrustedStreaming = meter.Measure(
                "forced-pending untrusted streaming",
                pendingStream,
                () => SendStreaming(pendingUntrusted, pendingStream));
            var pendingPeerOwned = meter.Measure(
                "forced-pending peer owned",
                pendingStream,
                () => SendOwned(pendingPeer, pendingStream));
            var pendingTrustedStreaming = meter.Measure(
                "forced-pending trusted streaming",
                pendingStream,
                () => SendStreaming(pendingTrusted, pendingStream));

            Console.WriteLine("Streaming frame validation probe");
            Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
            Console.WriteLine(
                $"verified frame bytes = {MessageFramer.HeaderSize}; " +
                $"per-frame checksum = {s_frameChecksum}");
            FrameSendProbeMeter.Write(synchronousOwned);
            FrameSendProbeMeter.Write(synchronousUntrustedStreaming);
            FrameSendProbeMeter.Write(synchronousPeerOwned);
            FrameSendProbeMeter.Write(synchronousTrustedStreaming);
            FrameSendProbeMeter.Write(pendingOwned);
            FrameSendProbeMeter.Write(pendingUntrustedStreaming);
            FrameSendProbeMeter.Write(pendingPeerOwned);
            FrameSendProbeMeter.Write(pendingTrustedStreaming);
            WriteOverhead(
                "synchronous untrusted overhead",
                synchronousOwned,
                synchronousUntrustedStreaming);
            WriteOverhead(
                "synchronous trusted overhead",
                synchronousPeerOwned,
                synchronousTrustedStreaming);
            WriteOverhead(
                "pending untrusted overhead",
                pendingOwned,
                pendingUntrustedStreaming);
            WriteOverhead(
                "pending trusted overhead",
                pendingPeerOwned,
                pendingTrustedStreaming);
        }
        finally
        {
            synchronousConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            pendingConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static RpcStreamFrameSender CreateUntrustedSender(StreamConnection connection) =>
        new(connection.SendAsync, connection.SendFrameValueAsync);

    private static RpcStreamFrameSender CreateTrustedSender(RpcPeerSender peer) =>
        new(
            peer.SendAsync,
            peer.ValidatedFrameSender ??
                throw new InvalidOperationException("Expected a validated built-in frame sender."));

    private static void SendOwned(StreamConnection connection, ControlledWriteStream stream)
    {
        var pending = connection.SendFrameValueAsync(CreateFrame());
        CompleteSend(pending, stream);
    }

    private static void SendOwned(RpcPeerSender peer, ControlledWriteStream stream)
    {
        var pending = peer.SendFrameValueAsync(CreateFrame(), CancellationToken.None);
        CompleteSend(pending, stream);
    }

    private static void SendStreaming(RpcStreamFrameSender sender, ControlledWriteStream stream)
    {
        var pending = sender.SendAsync(CreateFrame(), CancellationToken.None);
        CompleteSend(pending, stream);
    }

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

    private static PooledBufferWriter CreateFrame()
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static long CalculateFrameChecksum()
    {
        using var frame = CreateFrame();
        var checksum = 0L;
        for (var index = 0; index < frame.WrittenCount; index++)
        {
            checksum += (index + 1L) * frame.WrittenSpan[index];
        }

        return checksum;
    }

    private static void WriteOverhead(
        string name,
        FrameSendMeasurement control,
        FrameSendMeasurement streaming) =>
        Console.WriteLine(
            $"{name,-40} " +
            $"{streaming.NanosecondsPerOperation - control.NanosecondsPerOperation,9:N1} ns/op " +
            $"{streaming.BytesPerOperation - control.BytesPerOperation,9:N1} B/op");
}
