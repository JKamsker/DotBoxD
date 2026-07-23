using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

[Collection(TcpReceiveOperationCollection.Name)]
public sealed class TcpTransferredStateFallbackCoverageTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Transferred_fallback_preserves_success_cancellation_timeout_and_eof()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var pairs = new List<TcpReceiveTestPair>(capacity + 4);
        var holders = new List<ValueTask<RpcFrame>>(capacity);
        using var holderCancellation = new CancellationTokenSource();
        try
        {
            for (var index = 0; index < capacity; index++)
            {
                pairs.Add(await TcpReceiveTestPair.CreateAsync());
            }

            var reusablePair = await TcpReceiveTestPair.CreateAsync();
            var timeoutPair = await TcpReceiveTestPair.CreateAsync(TimeSpan.FromSeconds(2));
            var eofPair = await TcpReceiveTestPair.CreateAsync();
            var fromBeginningEofPair = await TcpReceiveTestPair.CreateAsync();
            pairs.Add(reusablePair);
            pairs.Add(timeoutPair);
            pairs.Add(eofPair);
            pairs.Add(fromBeginningEofPair);

            foreach (var pair in pairs.Take(capacity))
            {
                var holder = pair.Connection.ReceiveFrameValueAsync(holderCancellation.Token);
                Assert.False(holder.IsCompleted);
                holders.Add(holder);
            }

            Assert.True(TcpFrameReceiveOperationPopulation.IsAtCapacity);
            Assert.True(TcpFrameReceiveOperationPopulation.HasNoAvailableOperation());

            await VerifySuccessfulFallbackAsync(reusablePair);
            await VerifyPayloadFallbackAsync(reusablePair);
            await VerifyCanceledFallbackAsync(reusablePair);
            await VerifyTimedOutFallbackAsync(timeoutPair);
            await VerifyEofFallbackAsync(eofPair);
            await VerifyFromBeginningBodyEofFallbackAsync(fromBeginningEofPair);
        }
        finally
        {
            holderCancellation.Cancel();
            try
            {
                await DrainHoldersAsync(holders);
            }
            finally
            {
                await DisposePairsAsync(pairs);
            }
        }
    }

    private static async Task VerifySuccessfulFallbackAsync(TcpReceiveTestPair pair)
    {
        var expected = TcpReceiveTestPair.CreateFrame(messageId: 520);
        var pending = StartTransferredFallback(pair.Connection);

        await pair.QueueBytesAsync(expected.AsMemory(0, sizeof(int)));
        await pair.WaitForPrefixAsync(expected.Length);
        await pair.QueueBytesAsync(expected.AsMemory(sizeof(int)));

        using var frame = await pending.AsTask().WaitAsync(Guard);
        AssertFrame(frame, messageId: 520);
    }

    private static async Task VerifyCanceledFallbackAsync(TcpReceiveTestPair pair)
    {
        using var cancellation = new CancellationTokenSource();
        var pending = StartTransferredFallback(pair.Connection, cancellation.Token);

        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private static async Task VerifyPayloadFallbackAsync(TcpReceiveTestPair pair)
    {
        var expected = TcpReceiveTestPair.CreateFrame(messageId: 521);
        var pending = StartTransferredPayloadFallback(pair.Connection);

        await pair.QueueBytesAsync(expected);

        using var payload = await pending.AsTask().WaitAsync(Guard);
        Assert.Equal(expected, payload.Memory.ToArray());
    }

    private static async Task VerifyTimedOutFallbackAsync(TcpReceiveTestPair pair)
    {
        var pending = StartTransferredFallback(pair.Connection);

        var error = await Assert.ThrowsAsync<IOException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Contains("stalled", error.Message);
    }

    private static async Task VerifyEofFallbackAsync(TcpReceiveTestPair pair)
    {
        var pending = StartTransferredFallback(pair.Connection);
        await pair.QueueBytesAsync(new byte[] { 1, 2 });
        pair.Peer.Client.Shutdown(SocketShutdown.Send);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Equal("Connection closed after 2 of 4 frame length bytes.", error.Message);
    }

    private static async Task VerifyFromBeginningBodyEofFallbackAsync(TcpReceiveTestPair pair)
    {
        var expected = TcpReceiveTestPair.CreateFrame(messageId: 522);
        var pending = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(pending.IsCompleted);

        await pair.QueueBytesAsync(expected.AsMemory(0, sizeof(int) + 3));
        await pair.WaitForPrefixAsync(expected.Length);
        pair.Peer.Client.Shutdown(SocketShutdown.Send);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Equal(
            $"Connection closed after 3 of {expected.Length - sizeof(int)} frame bytes.",
            error.Message);
    }

    private static ValueTask<RpcFrame> StartTransferredFallback(
        TcpConnection connection,
        CancellationToken cancellationToken = default)
    {
        var capacityField = typeof(TcpFrameReceiveOperationPopulation).GetField(
            "_isAtCapacity",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TCP receive capacity state was not found.");
        var previous = Assert.IsType<bool>(capacityField.GetValue(null));
        Assert.True(previous);
        Assert.True(TcpFrameReceiveOperationPopulation.HasNoAvailableOperation());

        // Simulate the documented stale-false race: initialization has already started when the
        // lifetime creation budget reports exhaustion, so ownership transfers to ContinueAsync.
        capacityField.SetValue(null, false);
        try
        {
            var pending = connection.ReceiveFrameValueAsync(cancellationToken);
            Assert.False(pending.IsCompleted);
            return pending;
        }
        finally
        {
            capacityField.SetValue(null, previous);
        }
    }

    private static ValueTask<Payload> StartTransferredPayloadFallback(TcpConnection connection)
    {
        var capacityField = typeof(TcpFrameReceiveOperationPopulation).GetField(
            "_isAtCapacity",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TCP receive capacity state was not found.");
        var previous = Assert.IsType<bool>(capacityField.GetValue(null));
        Assert.True(previous);
        Assert.True(TcpFrameReceiveOperationPopulation.HasNoAvailableOperation());

        capacityField.SetValue(null, false);
        try
        {
            var pending = connection.ReceiveValueAsync();
            Assert.False(pending.IsCompleted);
            return pending;
        }
        finally
        {
            capacityField.SetValue(null, previous);
        }
    }

    private static async Task DrainHoldersAsync(IEnumerable<ValueTask<RpcFrame>> holders)
    {
        Exception? firstError = null;
        foreach (var holder in holders)
        {
            try
            {
                using var frame = await holder.AsTask().WaitAsync(Guard);
            }
            catch (OperationCanceledException)
            {
                // Expected cleanup for sources held only to exhaust the reusable population.
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
        }

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private static async Task DisposePairsAsync(IEnumerable<TcpReceiveTestPair> pairs)
    {
        Exception? firstError = null;
        foreach (var pair in pairs)
        {
            try
            {
                await pair.DisposeAsync();
            }
            catch (Exception error)
            {
                firstError ??= error;
            }
        }

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }
}
