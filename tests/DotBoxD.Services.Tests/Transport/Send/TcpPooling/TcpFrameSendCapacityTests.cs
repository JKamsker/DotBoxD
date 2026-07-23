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
        var pairs = new List<TcpSendTestPair>(capacity + 2);
        var sends = new List<ActiveSend>(capacity + 2);
        ActiveRawSend? rawFallback = null;
        try
        {
            for (var index = 0; index <= capacity + 1; index++)
            {
                pairs.Add(await TcpSendTestPair.CreateAsync());
            }

            var fallbackCount = TcpFrameSendFallback.StartedCountForTests;
            var rawFallbackCount = TcpFrameSendFallback.RawFromBeginningCountForTests;
            for (var index = 0; index < capacity; index++)
            {
                sends.Add(StartHeldWrite(pairs[index], index));
            }

            Assert.Equal(0, TcpFrameSendOperation.RetainedCountForTests);
            Assert.Equal(fallbackCount, TcpFrameSendFallback.StartedCountForTests);

            sends.Add(StartHeldWrite(pairs[capacity], capacity));
            Assert.Equal(fallbackCount + 1, TcpFrameSendFallback.StartedCountForTests);
            rawFallback = StartHeldRawFallback(pairs[capacity + 1]);
            Assert.Equal(
                rawFallbackCount + 1,
                TcpFrameSendFallback.RawFromBeginningCountForTests);
            Assert.Equal(fallbackCount + 1, TcpFrameSendFallback.StartedCountForTests);

            await CompleteAndConsumeAsync(sends);
            Assert.Equal(capacity, TcpFrameSendOperation.RetainedCountForTests);
            Assert.All(sends, static send => Assert.Equal(1, send.Source.GetResultCount));
            Assert.All(sends, static send => send.AssertRawFrameUnchanged());
            await rawFallback.CompleteAsync();

            var recoverySource = new ControlledPendingSend();
            var recoveryFrame = TcpSendTestFrames.CreateBytes(messageId: 799);
            Assert.True(pairs[0].Connection.SendGate.Wait(0));
            var recovered = TcpConnectionFrameSender.ContinuePendingWriteForTests(
                pairs[0].Connection,
                recoveryFrame,
                CancellationToken.None,
                recoverySource.Pending);
            var recovery = new ActiveSend(recoverySource, recovered, recoveryFrame);
            sends.Add(recovery);
            Assert.Equal(capacity - 1, TcpFrameSendOperation.RetainedCountForTests);
            Assert.Equal(fallbackCount + 1, TcpFrameSendFallback.StartedCountForTests);
            Assert.Equal(
                rawFallbackCount + 1,
                TcpFrameSendFallback.RawFromBeginningCountForTests);

            recoverySource.Succeed();
            await recovered.AsTask().WaitAsync(Guard);
            recovery.IsConsumed = true;
            Assert.Equal(capacity, TcpFrameSendOperation.RetainedCountForTests);
            recovery.AssertRawFrameUnchanged();

            Assert.True(TcpFrameSendOperation.RequiresPreflight);
            Assert.False(TcpFrameSendOperation.MustUseRawFallback());
            Assert.False(TcpFrameSendOperation.RequiresPreflight);
            var postCapacityFrame = TcpSendTestFrames.CreateBytes(messageId: 800);
            var postCapacitySend = pairs[0].Connection.SendValueAsync(postCapacityFrame);
            await postCapacitySend.AsTask().WaitAsync(Guard);
            Assert.False(TcpFrameSendOperation.RequiresPreflight);
            Assert.Equal(
                postCapacityFrame,
                await pairs[0].ReadAsync(postCapacityFrame.Length));
        }
        finally
        {
            if (rawFallback is not null)
            {
                await rawFallback.DrainAsync();
            }

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

    private static ActiveRawSend StartHeldRawFallback(TcpSendTestPair pair)
    {
        Assert.True(pair.Connection.SendGate.Wait(0));
        var frame = TcpSendTestFrames.CreateBytes(messageId: 798);
        var send = pair.Connection.SendValueAsync(frame);
        Assert.False(send.IsCompleted);
        return new ActiveRawSend(pair, frame, send);
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

    private sealed class ActiveSend(
        ControlledPendingSend source,
        ValueTask pending,
        byte[]? rawFrame = null)
    {
        private readonly byte[]? _expectedRawFrame = rawFrame?.ToArray();

        public bool IsConsumed { get; set; }

        public ValueTask Pending { get; } = pending;

        public byte[]? RawFrame { get; } = rawFrame;

        public ControlledPendingSend Source { get; } = source;

        public void AssertRawFrameUnchanged()
        {
            if (_expectedRawFrame is not null)
            {
                Assert.Equal(_expectedRawFrame, RawFrame);
            }
        }
    }

    private sealed class ActiveRawSend(
        TcpSendTestPair pair,
        byte[] frame,
        ValueTask pending)
    {
        private readonly byte[] _expected = frame.ToArray();
        private readonly Task _pending = pending.AsTask();
        private bool _gateHeld = true;
        private bool _consumed;

        public async Task CompleteAsync()
        {
            ReleaseGate();
            await _pending.WaitAsync(Guard);
            _consumed = true;
            Assert.Equal(_expected, frame);
            Assert.Equal(_expected, await pair.ReadAsync(_expected.Length));
        }

        public async Task DrainAsync()
        {
            ReleaseGate();
            if (_consumed)
            {
                return;
            }

            try
            {
                await _pending.WaitAsync(Guard);
                _consumed = true;
            }
            catch
            {
                // Preserve the original test failure while releasing the pending raw send.
            }
        }

        private void ReleaseGate()
        {
            if (!_gateHeld)
            {
                return;
            }

            pair.Connection.ReleaseSendGate();
            _gateHeld = false;
        }
    }
}
