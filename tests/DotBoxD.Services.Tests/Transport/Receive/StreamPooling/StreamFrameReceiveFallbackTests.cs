using System.Buffers.Binary;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Tests.Transport.Receive.Lookahead;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

[Collection(StreamReceiveOperationCollection.Name)]
public sealed class StreamFrameReceiveFallbackTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task LookaheadReceivesBeyondCapacity_TimeoutAndRecoverTogether()
    {
        var capacity = BoundedFrameReceiveOperationPool<object>.MaxRetainedCount;
        var streams = Enumerable.Range(0, capacity + 1)
            .Select(index => new ScriptedLookaheadReadStream(
                CreateFrame(600 + index),
                Array.Empty<int>(),
                gatedReadIndex: 0))
            .ToArray();
        var connections = streams
            .Select(stream => new StreamConnection(
                stream,
                ownsStream: true,
                frameReadIdleTimeout: TimeSpan.FromMilliseconds(100)))
            .ToArray();
        try
        {
            var timedOut = connections
                .Select(connection => connection.ReceiveFrameValueAsync())
                .ToArray();
            await Task.WhenAll(streams.Select(stream => stream.WaitForGatedReadAsync(Guard)));
            await Task.WhenAll(timedOut.Select(ObserveTimeoutAsync));

            foreach (var stream in streams)
            {
                stream.ReleaseGatedRead();
            }

            for (var index = 0; index < connections.Length; index++)
            {
                using var frame = await connections[index]
                    .ReceiveFrameValueAsync()
                    .AsTask()
                    .WaitAsync(Guard);
                AssertFrame(frame, 600 + index);
            }
        }
        finally
        {
            await DisposeConnectionsAsync(connections);
        }
    }

    [Fact]
    public async Task ExactReceivesBeyondCapacity_CancelAndRecoverTogether()
    {
        var capacity = BoundedFrameReceiveOperationPool<object>.MaxRetainedCount;
        var streams = Enumerable.Range(0, capacity + 1)
            .Select(index => new ScriptedLookaheadReadStream(
                CreateFrame(650 + index),
                Array.Empty<int>(),
                gatedReadIndex: 0))
            .ToArray();
        var connections = streams
            .Select(stream => new StreamConnection(
                stream,
                ownsStream: false,
                frameReadIdleTimeout: Timeout.InfiniteTimeSpan))
            .ToArray();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var canceled = connections
                .Select(connection => connection.ReceiveFrameValueAsync(cancellation.Token))
                .ToArray();
            await Task.WhenAll(streams.Select(stream => stream.WaitForGatedReadAsync(Guard)));
            cancellation.Cancel();
            await Task.WhenAll(canceled.Select(ObserveCancellationAsync));

            foreach (var stream in streams)
            {
                stream.ReleaseGatedRead();
            }

            for (var index = 0; index < connections.Length; index++)
            {
                using var frame = await connections[index]
                    .ReceiveFrameValueAsync()
                    .AsTask()
                    .WaitAsync(Guard);
                AssertFrame(frame, 650 + index);
            }
        }
        finally
        {
            await DisposeConnectionsAsync(connections);
        }
    }

    [Fact]
    public async Task SaturatedSources_FallbackPreservesEofAndCallerContext()
    {
        var capacity = BoundedFrameReceiveOperationPool<object>.MaxRetainedCount;
        var holderStreams = Enumerable.Range(0, capacity)
            .Select(index => new ControlledPendingFrameStream(CreateFrame(700 + index)))
            .ToArray();
        var holderConnections = holderStreams
            .Select(stream => new StreamConnection(
                stream,
                ownsStream: false,
                frameReadIdleTimeout: Timeout.InfiniteTimeSpan))
            .ToArray();
        var holders = holderConnections
            .Select(connection => connection.ReceiveFrameValueAsync())
            .ToArray();
        var previous = Context.Value;
        var holdersReleased = false;
        try
        {
            CompleteHolders(holderStreams);
            Assert.All(holders, holder => Assert.True(holder.IsCompleted));

            var partialFrame = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(partialFrame, 20);
            await using (var eofConnection = new StreamConnection(
                new MemoryStream(partialFrame, writable: false),
                ownsStream: true,
                frameReadIdleTimeout: Timeout.InfiniteTimeSpan))
            {
                var error = await Assert.ThrowsAsync<InvalidDataException>(
                    () => eofConnection.ReceiveFrameValueAsync().AsTask());
                Assert.Equal("Connection closed after 8 of 20 frame bytes.", error.Message);
            }

            Context.Value = "caller";
            var overflowStream = new ControlledPendingFrameStream(CreateFrame(750));
            await using var overflowConnection = new StreamConnection(
                overflowStream,
                ownsStream: false,
                frameReadIdleTimeout: Timeout.InfiniteTimeSpan);
            var overflow = overflowConnection.ReceiveFrameValueAsync();
            var completion = ObserveRawCompletion(overflow, expectedMessageId: 750);
            await Task.Run(() =>
            {
                Context.Value = "producer";
                overflowStream.CompleteNextRead();
                overflowStream.CompleteNextRead();
            });
            await completion.WaitAsync(Guard);

            for (var index = 0; index < holders.Length; index++)
            {
                using var frame = await holders[index].AsTask().WaitAsync(Guard);
                AssertFrame(frame, 700 + index);
            }

            holdersReleased = true;
        }
        finally
        {
            Context.Value = previous;
            if (!holdersReleased)
            {
                CompleteHoldersBestEffort(holderStreams);
            }

            await DisposeConnectionsAsync(holderConnections);
        }
    }

    private static async Task ObserveTimeoutAsync(ValueTask<RpcFrame> pending)
    {
        var timeout = await Assert.ThrowsAsync<IOException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Contains("stalled", timeout.Message);
    }

    private static async Task ObserveCancellationAsync(ValueTask<RpcFrame> pending) =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.AsTask().WaitAsync(Guard));

    private static Task ObserveRawCompletion(ValueTask<RpcFrame> pending, int expectedMessageId)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable xUnit1030 // This regression intentionally observes producer context.
        var awaiter = pending.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        awaiter.UnsafeOnCompleted(() =>
        {
            try
            {
                Assert.Equal("caller", Context.Value);
                using var frame = awaiter.GetResult();
                AssertFrame(frame, expectedMessageId);
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        });
        return completion.Task;
    }

    private static void CompleteHolders(IReadOnlyList<ControlledPendingFrameStream> streams)
    {
        foreach (var stream in streams)
        {
            stream.CompleteNextRead();
            stream.CompleteNextRead();
        }
    }

    private static void CompleteHoldersBestEffort(
        IReadOnlyList<ControlledPendingFrameStream> streams)
    {
        foreach (var stream in streams)
        {
            try
            {
                stream.CompleteNextRead();
                stream.CompleteNextRead();
            }
            catch
            {
                // Preserve the original test failure while releasing any remaining receive.
            }
        }
    }

    private static async Task DisposeConnectionsAsync(IEnumerable<StreamConnection> connections)
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private static byte[] CreateFrame(int messageId)
    {
        using var payload = MessageFramer.FrameToPayload(
            messageId,
            MessageType.Response,
            new byte[] { 1, 2, 3 });
        return payload.Memory.ToArray();
    }

    private static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }
}
