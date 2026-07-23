using DotBoxD.Services.Tests.Transport.Send.Pooling;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpFrameSendCapacityTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ActivePopulation_UsesTaskFallbackAtCapacityThenRecoversPool()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var pairs = new List<TcpSendTestPair>(capacity + 1);
        var sends = new List<ActiveSend>(capacity + 2);
        try
        {
            for (var index = 0; index <= capacity; index++)
            {
                pairs.Add(await TcpSendTestPair.CreateAsync());
            }

            var fallbackCount = TcpFrameSendFallback.StartedCountForTests;
            for (var index = 0; index < capacity; index++)
            {
                sends.Add(StartHeldWrite(pairs[index], index));
            }

            Assert.Equal(0, TcpFrameSendOperation.RetainedCountForTests);
            Assert.Equal(fallbackCount, TcpFrameSendFallback.StartedCountForTests);

            sends.Add(StartHeldWrite(pairs[capacity], capacity));
            Assert.Equal(fallbackCount + 1, TcpFrameSendFallback.StartedCountForTests);

            await CompleteAndConsumeAsync(sends);
            Assert.Equal(capacity, TcpFrameSendOperation.RetainedCountForTests);
            Assert.All(sends, static send => Assert.Equal(1, send.Source.GetResultCount));

            var recoverySource = new ControlledPendingSend();
            var recoveryFrame = TcpSendTestFrames.CreateOwned(messageId: 799);
            Assert.True(pairs[0].Connection.SendGate.Wait(0));
            var recovered = TcpConnectionFrameSender.ContinuePendingWriteForTests(
                pairs[0].Connection,
                recoveryFrame,
                CancellationToken.None,
                recoverySource.Pending);
            var recovery = new ActiveSend(recoverySource, recovered);
            sends.Add(recovery);
            Assert.Equal(capacity - 1, TcpFrameSendOperation.RetainedCountForTests);
            Assert.Equal(fallbackCount + 1, TcpFrameSendFallback.StartedCountForTests);

            recoverySource.Succeed();
            await recovered.AsTask().WaitAsync(Guard);
            recovery.IsConsumed = true;
            Assert.Equal(capacity, TcpFrameSendOperation.RetainedCountForTests);
            TcpSendTestFrames.AssertDisposed(recoveryFrame);
        }
        finally
        {
            await DrainAsync(sends);
            foreach (var pair in pairs)
            {
                await pair.DisposeAsync();
            }
        }
    }

    private static ActiveSend StartHeldWrite(
        TcpSendTestPair pair,
        int index)
    {
        Assert.True(pair.Connection.SendGate.Wait(0));
        var source = new ControlledPendingSend();
        var frame = TcpSendTestFrames.CreateOwned(700 + index);
        var send = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            CancellationToken.None,
            source.Pending);
        Assert.False(send.IsCompleted);
        return new ActiveSend(source, send);
    }

    private static async Task CompleteAndConsumeAsync(
        IEnumerable<ActiveSend> sends)
    {
        foreach (var send in sends)
        {
            send.Source.Succeed();
        }

        foreach (var send in sends)
        {
            await send.Pending.AsTask().WaitAsync(Guard);
            send.IsConsumed = true;
        }
    }

    private static async Task DrainAsync(IEnumerable<ActiveSend> sends)
    {
        foreach (var send in sends)
        {
            if (!send.Source.Pending.IsCompleted)
            {
                send.Source.Succeed();
            }

            if (send.IsConsumed)
            {
                continue;
            }

            try
            {
                await send.Pending.AsTask().WaitAsync(Guard);
                send.IsConsumed = true;
            }
            catch
            {
                // Preserve the original test failure while releasing transport ownership.
            }
        }
    }

    private sealed class ActiveSend(ControlledPendingSend source, ValueTask pending)
    {
        public bool IsConsumed { get; set; }

        public ValueTask Pending { get; } = pending;

        public ControlledPendingSend Source { get; } = source;
    }
}
