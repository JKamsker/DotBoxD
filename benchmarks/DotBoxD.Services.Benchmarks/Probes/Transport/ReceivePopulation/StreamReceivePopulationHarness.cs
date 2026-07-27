using System.Diagnostics;
using System.IO.Pipes;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class StreamReceivePopulationHarness : IAsyncDisposable
{
    private readonly StreamReceivePopulationPeer[] _peers;
    private readonly ValueTask<RpcFrame>[] _receives;
    private readonly StreamReceivePopulationInlineLane[] _rearmLanes;

    private StreamReceivePopulationHarness(
        StreamReceivePopulationPeer[] peers,
        int sharedCapacity)
    {
        _peers = peers;
        _receives = new ValueTask<RpcFrame>[peers.Length];
        var overflowCount = Math.Max(0, peers.Length - sharedCapacity);
        _rearmLanes = new StreamReceivePopulationInlineLane[overflowCount];
        for (var index = 0; index < _rearmLanes.Length; index++)
        {
            _rearmLanes[index] = new StreamReceivePopulationInlineLane(
                peers[sharedCapacity + index]);
        }
    }

    public int PeerCount => _peers.Length;

    public static async Task<StreamReceivePopulationHarness> CreateAsync(
        int peerCount,
        int sharedCapacity)
    {
        var peers = new StreamReceivePopulationPeer[peerCount];
        var createdCount = 0;
        try
        {
            for (; createdCount < peers.Length; createdCount++)
            {
                peers[createdCount] = await CreatePeerAsync(createdCount).ConfigureAwait(false);
            }

            return new StreamReceivePopulationHarness(peers, sharedCapacity);
        }
        catch
        {
            await DisposeAsync(peers, createdCount).ConfigureAwait(false);
            throw;
        }
    }

    public StreamConnection GetConnection(int index) => _peers[index].Connection;

    public void PrimePipes()
    {
        for (var index = 0; index < _peers.Length; index++)
        {
            _receives[index] = StreamReceivePopulationIo.StartPending(_peers[index]);
            StreamReceivePopulationIo.Send(_peers[index]);
            StreamReceivePopulationIo.Consume(_peers[index], _receives[index]);
        }
    }

    public void RunRound()
    {
        StartAll();
        CompleteAll();
    }

    public void RunRounds(int count)
    {
        for (var round = 0; round < count; round++)
        {
            RunRound();
        }
    }

    public StreamReceivePopulationRun MeasureStartCost(int rounds)
    {
        long callerAllocatedBytes = 0;
        long totalAllocatedBytes = 0;
        long elapsedTicks = 0;
        for (var round = 0; round < rounds; round++)
        {
            var callerAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            StartAll();
            elapsedTicks += Stopwatch.GetTimestamp() - started;
            callerAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - callerAllocatedBefore;
            totalAllocatedBytes += GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore;
            CompleteAll();
        }

        return new StreamReceivePopulationRun(
            checked(rounds * _peers.Length),
            elapsedTicks,
            callerAllocatedBytes,
            totalAllocatedBytes);
    }

    public void ActivateDedicatedLanes()
    {
        var sharedCount = _peers.Length - _rearmLanes.Length;
        for (var index = 0; index < sharedCount; index++)
        {
            _receives[index] = StreamReceivePopulationIo.StartPending(_peers[index]);
        }

        foreach (var lane in _rearmLanes)
        {
            lane.Start();
        }

        foreach (var lane in _rearmLanes)
        {
            lane.SendFirst();
        }

        foreach (var lane in _rearmLanes)
        {
            lane.WaitForSuccessor();
            lane.SendSuccessor();
        }

        foreach (var lane in _rearmLanes)
        {
            lane.ConsumeSuccessor();
        }

        for (var index = 0; index < sharedCount; index++)
        {
            StreamReceivePopulationIo.Send(_peers[index]);
            StreamReceivePopulationIo.Consume(_peers[index], _receives[index]);
        }
    }

    public void ClearScratch()
    {
        Array.Clear(_receives);
        foreach (var lane in _rearmLanes)
        {
            lane.Clear();
        }
    }

    public void KeepAlive() => GC.KeepAlive(_peers);

    public async ValueTask DisposeAsync() =>
        await DisposeAsync(_peers, _peers.Length).ConfigureAwait(false);

    private static async Task<StreamReceivePopulationPeer> CreatePeerAsync(int index)
    {
        var pipeName = $"dotboxd-stream-population-{Guid.NewGuid():N}";
        var receiver = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var sender = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            var accepting = receiver.WaitForConnectionAsync();
            await sender.ConnectAsync().ConfigureAwait(false);
            await accepting.ConfigureAwait(false);
            return new StreamReceivePopulationPeer(
                new StreamConnection(
                    receiver,
                    ownsStream: true,
                    frameReadIdleTimeout: Timeout.InfiniteTimeSpan),
                sender,
                ReceivePopulationFrame.Create(messageId: 20_000 + index));
        }
        catch
        {
            receiver.Dispose();
            sender.Dispose();
            throw;
        }
    }

    private void StartAll()
    {
        for (var index = 0; index < _peers.Length; index++)
        {
            _receives[index] = StreamReceivePopulationIo.StartPending(_peers[index]);
        }
    }

    private void CompleteAll()
    {
        for (var index = 0; index < _peers.Length; index++)
        {
            StreamReceivePopulationIo.Send(_peers[index]);
        }

        for (var index = 0; index < _peers.Length; index++)
        {
            StreamReceivePopulationIo.Consume(_peers[index], _receives[index]);
        }
    }

    private static async ValueTask DisposeAsync(
        StreamReceivePopulationPeer[] peers,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            await peers[index].Connection.DisposeAsync().ConfigureAwait(false);
            peers[index].Sender.Dispose();
        }
    }
}
