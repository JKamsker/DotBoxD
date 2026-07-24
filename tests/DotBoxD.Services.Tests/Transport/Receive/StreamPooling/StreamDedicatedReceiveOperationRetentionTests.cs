using System.Runtime.CompilerServices;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

[Collection(StreamReceiveOperationCollection.Name)]
public sealed class StreamDedicatedReceiveOperationRetentionTests
{
    [Fact]
    public async Task CompletedUnconsumedDedicatedResult_DoesNotRetainConnectionGraph()
    {
        var probe = await CreateProbeAsync();

        ForceGc();

        Assert.False(probe.Connection.IsAlive);
        Assert.False(probe.Receiver.IsAlive);
        Assert.False(probe.Stream.IsAlive);
        Assert.False(probe.CallerCancellation.IsAlive);
        using var frame = await probe.Pending;
        StreamDedicatedReceiveOperationTests.AssertFrame(frame, messageId: 960);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<RetentionProbe> CreateProbeAsync()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = await StreamReceiveTestPair.CreateAsync();
        try
        {
            var admission = pair.Connection.ReceiveFrameValueAsync();
            await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 959));
            using (var frame = await admission.AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
            {
                StreamDedicatedReceiveOperationTests.AssertFrame(frame, messageId: 959);
            }

            Assert.True(pair.Connection.HasDedicatedReceiveCache);
            using var callerCancellation = new CancellationTokenSource();
            var pending = pair.Connection.ReceiveFrameValueAsync(callerCancellation.Token);
            await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 960));
            await WaitForCompletionAsync(pending);

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

    private static async Task WaitForCompletionAsync(ValueTask<RpcFrame> pending)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!pending.IsCompleted)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The dedicated Stream receive did not complete.");
            }

            await Task.Yield();
        }
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
        WeakReference Receiver,
        WeakReference Stream,
        WeakReference CallerCancellation);
}
