using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamReceivePopulationIo
{
    private static readonly TimeSpan ReceiveGuard = TimeSpan.FromSeconds(5);

    public static ValueTask<RpcFrame> StartPending(StreamReceivePopulationPeer peer)
    {
        var receive = peer.Connection.ReceiveFrameValueAsync();
        if (receive.IsCompleted)
        {
            throw new InvalidOperationException(
                "Stream population receive completed before its peer wrote a frame.");
        }

        return receive;
    }

    public static void Send(StreamReceivePopulationPeer peer) => peer.Sender.Write(peer.Frame);

    public static void Consume(StreamReceivePopulationPeer peer, ValueTask<RpcFrame> receive)
    {
        WaitForCompletion(receive);
        using var frame = receive.GetAwaiter().GetResult();
        ValidateFrame(peer, frame);
    }

    public static void ValidateFrame(StreamReceivePopulationPeer peer, RpcFrame frame)
    {
        if (!frame.Memory.Span.SequenceEqual(peer.Frame))
        {
            throw new InvalidOperationException(
                $"Stream population peer {peer.MessageId} received a stale or malformed frame.");
        }
    }

    private static void WaitForCompletion(ValueTask<RpcFrame> receive)
    {
        var started = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (!receive.IsCompleted)
        {
            if (Stopwatch.GetElapsedTime(started) >= ReceiveGuard)
            {
                throw new TimeoutException("Stream population receive did not complete.");
            }

            spinner.SpinOnce();
        }
    }
}

internal sealed class StreamReceivePopulationInlineLane
{
    private static readonly TimeSpan RearmGuard = TimeSpan.FromSeconds(5);

    private readonly StreamReceivePopulationPeer _peer;
    private readonly Action _continuation;
    private ConfiguredValueTaskAwaitable<RpcFrame>.ConfiguredValueTaskAwaiter _awaiter;
    private ValueTask<RpcFrame> _successor;
    private Exception? _error;
    private int _successorReady;

    public StreamReceivePopulationInlineLane(StreamReceivePopulationPeer peer)
    {
        _peer = peer;
        _continuation = CompleteFirst;
    }

    public void Start()
    {
        _error = null;
        Volatile.Write(ref _successorReady, 0);
        _awaiter = StreamReceivePopulationIo.StartPending(_peer)
            .ConfigureAwait(false)
            .GetAwaiter();
        _awaiter.UnsafeOnCompleted(_continuation);
    }

    public void SendFirst() => StreamReceivePopulationIo.Send(_peer);

    public void WaitForSuccessor()
    {
        var started = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (Volatile.Read(ref _successorReady) == 0)
        {
            if (Stopwatch.GetElapsedTime(started) >= RearmGuard)
            {
                throw new TimeoutException("Stream inline receive rearm did not complete.");
            }

            spinner.SpinOnce();
        }

        if (_error is not null)
        {
            ExceptionDispatchInfo.Capture(_error).Throw();
        }
    }

    public void SendSuccessor() => StreamReceivePopulationIo.Send(_peer);

    public void ConsumeSuccessor() => StreamReceivePopulationIo.Consume(_peer, _successor);

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
            StreamReceivePopulationIo.ValidateFrame(_peer, frame);
            _successor = StreamReceivePopulationIo.StartPending(_peer);
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

internal readonly record struct StreamReceivePopulationPeer(
    StreamConnection Connection,
    NamedPipeClientStream Sender,
    byte[] Frame)
{
    public int MessageId => BitConverter.ToInt32(Frame, sizeof(int));
}

internal readonly record struct StreamReceivePopulationRun(
    int FrameCount,
    long ElapsedTicks,
    long CallerAllocatedBytes,
    long TotalAllocatedBytes)
{
    public double NanosecondsPerFrame =>
        ElapsedTicks * (1_000_000_000d / Stopwatch.Frequency) / FrameCount;

    public double CallerBytesPerFrame => CallerAllocatedBytes / (double)FrameCount;

    public double TotalBytesPerFrame => TotalAllocatedBytes / (double)FrameCount;
}
