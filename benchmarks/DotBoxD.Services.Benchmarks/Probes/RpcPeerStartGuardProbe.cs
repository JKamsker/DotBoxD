using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Benchmarks.Support;
using DotBoxD.Services.Peer;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcPeerStartGuardProbe
{
    private const int WarmupIterations = 100_000;
    private const int Iterations = 10_000_000;

    public static void Run()
    {
        var (leftConnection, rightConnection) = InMemoryPipe.CreateConnectionPair();
        var peer = RpcPeer.Over(leftConnection, new MessagePackRpcSerializer()).Start();

        try
        {
            for (var i = 0; i < WarmupIterations; i++)
            {
                _ = peer.Start();
            }

            ForceGc();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            RpcPeer? last = null;
            for (var i = 0; i < Iterations; i++)
            {
                last = peer.Start();
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (!ReferenceEquals(last, peer))
            {
                throw new InvalidOperationException("Start stopped returning the same peer instance.");
            }

            Console.WriteLine("RpcPeer started-state guard probe");
            Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
            Console.WriteLine(
                $"Repeated Start             {elapsed.TotalMilliseconds,8:N1} ms " +
                $"{elapsed.TotalNanoseconds / Iterations,8:N1} ns/op " +
                $"{allocated,12:N0} B {allocated / (double)Iterations,8:N1} B/op");
        }
        finally
        {
            peer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            rightConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
