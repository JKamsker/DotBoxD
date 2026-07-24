using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TcpReceivePopulationIo
{
    private static readonly TimeSpan ReceiveGuard = TimeSpan.FromSeconds(5);

    public static ValueTask<RpcFrame> StartPending(TcpReceivePopulationPeer peer)
    {
        var receive = peer.Connection.ReceiveFrameValueAsync();
        if (receive.IsCompleted)
        {
            throw new InvalidOperationException(
                "TCP population receive completed before its peer wrote a frame.");
        }

        return receive;
    }

    public static void Send(TcpReceivePopulationPeer peer)
    {
        var remaining = peer.Frame.AsSpan();
        while (!remaining.IsEmpty)
        {
            var sent = peer.Sender.Client.Send(remaining, SocketFlags.None);
            if (sent <= 0)
            {
                throw new IOException("TCP probe peer closed during a frame send.");
            }

            remaining = remaining[sent..];
        }
    }

    public static void Consume(
        TcpReceivePopulationPeer peer,
        ValueTask<RpcFrame> receive)
    {
        WaitForCompletion(receive);
        using var frame = receive.GetAwaiter().GetResult();
        ValidateFrame(peer, frame);
    }

    public static void ValidateFrame(TcpReceivePopulationPeer peer, RpcFrame frame)
    {
        if (!frame.Memory.Span.SequenceEqual(peer.Frame))
        {
            throw new InvalidOperationException(
                $"TCP population peer {peer.MessageId} received a stale or malformed frame.");
        }
    }

    public static byte[] CreateFrame(int messageId)
    {
        var body = new byte[16];
        for (var index = 0; index < body.Length; index++)
        {
            body[index] = unchecked((byte)(messageId + index));
        }

        using var payload = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        return payload.Memory.ToArray();
    }

    private static void WaitForCompletion(ValueTask<RpcFrame> receive)
    {
        var started = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (!receive.IsCompleted)
        {
            if (Stopwatch.GetElapsedTime(started) >= ReceiveGuard)
            {
                throw new TimeoutException("TCP population receive did not complete.");
            }

            spinner.SpinOnce();
        }
    }
}

internal sealed class TcpReceivePopulationInlineLane
{
    private static readonly TimeSpan RearmGuard = TimeSpan.FromSeconds(5);

    private readonly TcpReceivePopulationPeer _peer;
    private readonly Action _continuation;
    private ConfiguredValueTaskAwaitable<RpcFrame>.ConfiguredValueTaskAwaiter _awaiter;
    private ValueTask<RpcFrame> _successor;
    private Exception? _error;
    private int _successorReady;

    public TcpReceivePopulationInlineLane(TcpReceivePopulationPeer peer)
    {
        _peer = peer;
        _continuation = CompleteFirst;
    }

    public void Start()
    {
        _error = null;
        Volatile.Write(ref _successorReady, 0);
        _awaiter = TcpReceivePopulationIo.StartPending(_peer)
            .ConfigureAwait(false)
            .GetAwaiter();
        _awaiter.UnsafeOnCompleted(_continuation);
    }

    public void SendFirst() => TcpReceivePopulationIo.Send(_peer);

    public void WaitForSuccessor()
    {
        var started = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (Volatile.Read(ref _successorReady) == 0)
        {
            if (Stopwatch.GetElapsedTime(started) >= RearmGuard)
            {
                throw new TimeoutException("TCP inline receive rearm did not complete.");
            }

            spinner.SpinOnce();
        }

        if (_error is not null)
        {
            ExceptionDispatchInfo.Capture(_error).Throw();
        }
    }

    public void SendSuccessor() => TcpReceivePopulationIo.Send(_peer);

    public void ConsumeSuccessor() => TcpReceivePopulationIo.Consume(_peer, _successor);

    public void Clear()
    {
        _awaiter = default;
        _successor = default;
        _error = null;
    }

    private void CompleteFirst()
    {
        try
        {
            using var frame = _awaiter.GetResult();
            TcpReceivePopulationIo.ValidateFrame(_peer, frame);
            _successor = TcpReceivePopulationIo.StartPending(_peer);
        }
        catch (Exception error)
        {
            _error = error;
        }
        finally
        {
            Volatile.Write(ref _successorReady, 1);
        }
    }
}

internal readonly record struct TcpReceivePopulationPeer(
    TcpConnection Connection,
    TcpClient Sender,
    byte[] Frame)
{
    public int MessageId => BitConverter.ToInt32(Frame, sizeof(int));
}

internal readonly record struct TcpReceivePopulationRun(
    int FrameCount,
    long ElapsedTicks,
    long AllocatedBytes)
{
    public double NanosecondsPerFrame =>
        ElapsedTicks * (1_000_000_000d / Stopwatch.Frequency) / FrameCount;

    public double BytesPerFrame => AllocatedBytes / (double)FrameCount;
}
