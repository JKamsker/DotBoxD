using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

[Collection(TcpReceiveOperationCollection.Name)]
public sealed class TcpDedicatedReceiveOperationRetentionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CompletedUnconsumedDedicatedResult_DoesNotRetainConnectionGraph()
    {
        var probe = await CreateProbeAsync();

        ForceGc();

        Assert.False(probe.Connection.IsAlive);
        Assert.False(probe.Client.IsAlive);
        Assert.False(probe.Stream.IsAlive);
        Assert.False(probe.CallerCancellation.IsAlive);
        using var frame = await probe.Pending;
        AssertFrame(frame, messageId: 930);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<RetentionProbe> CreateProbeAsync()
    {
        await using var population = await TcpSaturatedReceivePopulation.CreateAsync();
        var pair = await TcpReceiveTestPair.CreateAsync();
        try
        {
            var admission = pair.Connection.ReceiveFrameValueAsync();
            await pair.QueueBytesAsync(TcpReceiveTestPair.CreateFrame(messageId: 929));
            using (var frame = await admission.AsTask().WaitAsync(Guard))
            {
                AssertFrame(frame, messageId: 929);
            }

            Assert.True(pair.Connection.HasDedicatedReceiveCache);
            using var callerCancellation = new CancellationTokenSource();
            var pending = pair.Connection.ReceiveFrameValueAsync(callerCancellation.Token);
            await pair.QueueBytesAsync(TcpReceiveTestPair.CreateFrame(messageId: 930));
            var deadline = DateTime.UtcNow + Guard;
            while (!pending.IsCompleted)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("The dedicated TCP receive did not complete.");
                }

                await Task.Yield();
            }

            var probe = new RetentionProbe(
                pending,
                new WeakReference(pair.Connection),
                new WeakReference(pair.Receiver),
                new WeakReference(pair.Connection.FrameReceiveStream),
                new WeakReference(callerCancellation));
            pair.Peer.Dispose();
            return probe;
        }
        catch
        {
            await pair.DisposeAsync();
            throw;
        }
    }

    private static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record RetentionProbe(
        ValueTask<RpcFrame> Pending,
        WeakReference Connection,
        WeakReference Client,
        WeakReference Stream,
        WeakReference CallerCancellation);
}
