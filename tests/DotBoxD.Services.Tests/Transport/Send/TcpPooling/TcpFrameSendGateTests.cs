using DotBoxD.Services.Buffers;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpFrameSendGateTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task HeldGate_SuspendsOwnedSendThenWritesAndReleasesOwnership()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var expected = TcpSendTestFrames.CreateBytes(messageId: 601);
        var frame = CreateOwned(expected);
        Assert.True(pair.Connection.SendGate.Wait(0));

        var send = pair.Connection.SendFrameValueAsync(frame);
        Assert.False(send.IsCompleted);
        Assert.Equal(expected, frame.WrittenMemory.ToArray());

        pair.Connection.ReleaseSendGate();
        await send.AsTask().WaitAsync(Guard);

        TcpSendTestFrames.AssertDisposed(frame);
        Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        Assert.Equal(expected, await pair.ReadAsync(expected.Length));
    }

    [Fact]
    public async Task CallerCancellationAtHeldGate_PreservesTokenAndDoesNotReleaseGate()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        using var cancellation = new CancellationTokenSource();
        var frame = TcpSendTestFrames.CreateOwned(messageId: 602);
        var send = pair.Connection.SendFrameValueAsync(frame, cancellation.Token).AsTask();

        try
        {
            Assert.False(send.IsCompleted);
            cancellation.Cancel();

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => send.WaitAsync(Guard));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.True(send.IsCanceled);
            TcpSendTestFrames.AssertDisposed(frame);
            Assert.Equal(0, pair.Connection.SendGate.CurrentCount);
        }
        finally
        {
            pair.Connection.ReleaseSendGate();
        }
    }

    [Fact]
    public async Task DisposalAtHeldGate_FailsOwnedSendAndLeavesTerminalPermit()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        Assert.True(pair.Connection.SendGate.Wait(0));
        var frame = TcpSendTestFrames.CreateOwned(messageId: 603);
        var send = pair.Connection.SendFrameValueAsync(frame).AsTask();

        try
        {
            Assert.False(send.IsCompleted);
            await pair.Connection.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => send.WaitAsync(Guard));
            TcpSendTestFrames.AssertDisposed(frame);
            Assert.Equal(1, pair.Connection.SendGate.CurrentCount);
        }
        finally
        {
            pair.Connection.ReleaseSendGate();
        }
    }

    [Fact]
    public async Task CancelingOwnedWaiter_DoesNotChangeRawSendBehavior()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var rawFrame = TcpSendTestFrames.CreateBytes(messageId: 604);
        Assert.True(pair.Connection.SendGate.Wait(0));
        var rawSend = pair.Connection.SendValueAsync(rawFrame).AsTask();
        using var cancellation = new CancellationTokenSource();
        var ownedFrame = TcpSendTestFrames.CreateOwned(messageId: 605);
        var ownedSend = pair.Connection
            .SendFrameValueAsync(ownedFrame, cancellation.Token)
            .AsTask();

        Assert.False(rawSend.IsCompleted);
        Assert.False(ownedSend.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ownedSend.WaitAsync(Guard));
        TcpSendTestFrames.AssertDisposed(ownedFrame);

        pair.Connection.ReleaseSendGate();
        await rawSend.WaitAsync(Guard);
        Assert.Equal(rawFrame, await pair.ReadAsync(rawFrame.Length));
    }

    private static PooledBufferWriter CreateOwned(ReadOnlySpan<byte> bytes)
    {
        var frame = new PooledBufferWriter(bytes.Length);
        bytes.CopyTo(frame.GetSpan(bytes.Length));
        frame.Advance(bytes.Length);
        return frame;
    }
}
