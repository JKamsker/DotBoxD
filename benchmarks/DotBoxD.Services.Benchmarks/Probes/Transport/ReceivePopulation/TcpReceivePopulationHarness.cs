using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class TcpReceivePopulationHarness : IAsyncDisposable
{
    private readonly TcpReceivePopulationPeer[] _peers;
    private readonly ValueTask<RpcFrame>[] _receives;
    private readonly TcpReceivePopulationInlineLane[] _rearmLanes;

    private TcpReceivePopulationHarness(
        TcpReceivePopulationPeer[] peers,
        int sharedCapacity)
    {
        _peers = peers;
        _receives = new ValueTask<RpcFrame>[peers.Length];
        var overflowCount = Math.Max(0, peers.Length - sharedCapacity);
        _rearmLanes = new TcpReceivePopulationInlineLane[overflowCount];
        for (var index = 0; index < _rearmLanes.Length; index++)
        {
            _rearmLanes[index] = new TcpReceivePopulationInlineLane(
                peers[sharedCapacity + index]);
        }
    }

    public int PeerCount => _peers.Length;

    public static async Task<TcpReceivePopulationHarness> CreateAsync(
        int peerCount,
        int sharedCapacity)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start(peerCount);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var peers = new TcpReceivePopulationPeer[peerCount];
        var createdCount = 0;
        try
        {
            for (; createdCount < peers.Length; createdCount++)
            {
                var sender = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
                var accepting = listener.AcceptTcpClientAsync();
                await sender.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
                var receiver = await accepting.ConfigureAwait(false);
                receiver.NoDelay = true;
                peers[createdCount] = new TcpReceivePopulationPeer(
                    new TcpConnection(receiver, Timeout.InfiniteTimeSpan),
                    sender,
                    ReceivePopulationFrame.Create(messageId: 10_000 + createdCount));
            }

            return new TcpReceivePopulationHarness(peers, sharedCapacity);
        }
        catch
        {
            await DisposeAsync(peers, createdCount).ConfigureAwait(false);
            throw;
        }
    }

    public TcpConnection GetConnection(int index) => _peers[index].Connection;

    public void PrimeSockets()
    {
        for (var index = 0; index < _peers.Length; index++)
        {
            _receives[index] = TcpReceivePopulationIo.StartPending(_peers[index]);
            TcpReceivePopulationIo.Send(_peers[index]);
            TcpReceivePopulationIo.Consume(_peers[index], _receives[index]);
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

    public TcpReceivePopulationRun MeasureStartCost(int rounds)
    {
        long allocatedBytes = 0;
        long elapsedTicks = 0;
        for (var round = 0; round < rounds; round++)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            StartAll();
            elapsedTicks += Stopwatch.GetTimestamp() - started;
            allocatedBytes += GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            CompleteAll();
        }

        return new TcpReceivePopulationRun(
            checked(rounds * _peers.Length),
            elapsedTicks,
            allocatedBytes);
    }

    public void ActivateDedicatedLanes()
    {
        var sharedCount = _peers.Length - _rearmLanes.Length;
        for (var index = 0; index < sharedCount; index++)
        {
            _receives[index] = TcpReceivePopulationIo.StartPending(_peers[index]);
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
            TcpReceivePopulationIo.Send(_peers[index]);
            TcpReceivePopulationIo.Consume(_peers[index], _receives[index]);
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

    private void StartAll()
    {
        for (var index = 0; index < _peers.Length; index++)
        {
            _receives[index] = TcpReceivePopulationIo.StartPending(_peers[index]);
        }
    }

    private void CompleteAll()
    {
        for (var index = 0; index < _peers.Length; index++)
        {
            TcpReceivePopulationIo.Send(_peers[index]);
        }

        for (var index = 0; index < _peers.Length; index++)
        {
            TcpReceivePopulationIo.Consume(_peers[index], _receives[index]);
        }
    }

    private static async ValueTask DisposeAsync(
        TcpReceivePopulationPeer[] peers,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            await peers[index].Connection.DisposeAsync().ConfigureAwait(false);
            peers[index].Sender.Dispose();
        }
    }
}
