using DotBoxD.Services.Tests.Transport.Send.Pooling;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpFrameSendPreflightRaceTests
{
    private const int RaceRounds = 64;
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ConcurrentReturnAndPreflight_RearmsAndHealsPopulationHint()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var pairs = new List<TcpSendTestPair>(capacity + 1);
        var sends = new List<ActiveRawSend>(capacity + RaceRounds);
        try
        {
            for (var index = 0; index <= capacity; index++)
            {
                pairs.Add(await TcpSendTestPair.CreateAsync());
            }

            for (var index = 0; index < capacity; index++)
            {
                sends.Add(StartRawWrite(pairs[index], 900 + index));
            }

            sends.Add(StartRawWrite(pairs[capacity], 900 + capacity));
            Assert.True(TcpFrameSendOperation.IsAtCapacity);
            Assert.True(TcpFrameSendOperation.RequiresPreflight);
            Assert.Equal(0, TcpFrameSendOperation.RetainedCountForTests);

            var raced = sends[0];
            for (var round = 0; round < RaceRounds; round++)
            {
                await RaceReturnAgainstPreflightAsync(raced);
                Assert.Equal(1, TcpFrameSendOperation.RetainedCountForTests);

                Assert.False(TcpFrameSendOperation.MustUseRawFallback());
                Assert.False(TcpFrameSendOperation.RequiresPreflight);
                if (round == RaceRounds - 1)
                {
                    break;
                }

                raced = StartRawWrite(pairs[0], 1_000 + round);
                sends.Add(raced);
                Assert.Equal(0, TcpFrameSendOperation.RetainedCountForTests);
                Assert.True(TcpFrameSendOperation.RequiresPreflight);
            }

            await CompleteRemainingAsync(sends);
            Assert.Equal(capacity, TcpFrameSendOperation.RetainedCountForTests);
            Assert.All(sends, static send => send.AssertCallerMemoryUnchanged());
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

    private static ActiveRawSend StartRawWrite(TcpSendTestPair pair, int messageId)
    {
        Assert.True(pair.Connection.SendGate.Wait(0));
        var source = new ControlledPendingSend();
        var frame = TcpSendTestFrames.CreateBytes(messageId);
        var pending = TcpConnectionFrameSender.ContinuePendingWriteForTests(
            pair.Connection,
            frame,
            CancellationToken.None,
            source.Pending);
        Assert.False(pending.IsCompleted);
        return new ActiveRawSend(source, pending, frame);
    }

    private static async Task RaceReturnAgainstPreflightAsync(ActiveRawSend send)
    {
        using var start = new Barrier(participantCount: 2);
        var completion = Task.Run(async () =>
        {
            Signal(start);
            await send.CompleteAsync();
        });
        var preflight = Task.Run(() =>
        {
            Signal(start);
            return TcpFrameSendOperation.MustUseRawFallback();
        });

        await Task.WhenAll(completion, preflight).WaitAsync(Guard);
        _ = await preflight;
    }

    private static async Task CompleteRemainingAsync(IEnumerable<ActiveRawSend> sends)
    {
        foreach (var send in sends)
        {
            if (!send.IsConsumed)
            {
                await send.CompleteAsync();
            }
        }
    }

    private static async Task DrainAsync(IEnumerable<ActiveRawSend> sends)
    {
        foreach (var send in sends)
        {
            if (send.IsConsumed)
            {
                continue;
            }

            try
            {
                await send.CompleteAsync();
            }
            catch
            {
                // Preserve the original test failure while releasing transport ownership.
            }
        }
    }

    private static void Signal(Barrier start)
    {
        if (!start.SignalAndWait(Guard))
        {
            throw new TimeoutException("The preflight race did not reach both participants.");
        }
    }

    private sealed class ActiveRawSend(
        ControlledPendingSend source,
        ValueTask pending,
        byte[] frame)
    {
        private readonly byte[] _expected = frame.ToArray();
        private readonly Task _pending = pending.AsTask();

        public bool IsConsumed { get; private set; }

        public async Task CompleteAsync()
        {
            source.Succeed();
            await _pending.WaitAsync(Guard);
            // Let an inline AsTask consumer unwind the producer's pool-return finally.
            await Task.Yield();
            IsConsumed = true;
            AssertCallerMemoryUnchanged();
        }

        public void AssertCallerMemoryUnchanged() => Assert.Equal(_expected, frame);
    }
}
