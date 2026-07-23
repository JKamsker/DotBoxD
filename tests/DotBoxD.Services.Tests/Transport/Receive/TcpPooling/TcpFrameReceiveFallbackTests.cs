using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

public sealed class TcpFrameReceiveFallbackTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ReceivesBeyondPoolCapacity_TimeoutCancelAndRecoverTogether()
    {
        var capacity = BoundedFrameReceiveOperationPool<object>.MaxRetainedCount;
        var pairs = new List<TcpReceiveTestPair>(capacity + 1);
        var previous = Context.Value;
        try
        {
            for (var index = 0; index <= capacity; index++)
            {
                pairs.Add(await TcpReceiveTestPair.CreateAsync(TimeSpan.FromSeconds(2)));
            }

            var stalledFrames = pairs
                .Select((_, index) => TcpReceiveTestPair.CreateFrame(470 + index, bodyLength: 8))
                .ToArray();
            var timedOut = pairs
                .Select(pair => pair.Connection.ReceiveFrameValueAsync())
                .ToArray();
            Assert.All(timedOut, receive => Assert.False(receive.IsCompleted));
            await QueueAndObservePrefixesAsync(pairs, stalledFrames);
            await Task.WhenAll(timedOut.Select(ObserveTimeoutAsync));

            using var cancellation = new CancellationTokenSource();
            var canceledFrames = pairs
                .Select((_, index) => TcpReceiveTestPair.CreateFrame(490 + index, bodyLength: 9))
                .ToArray();
            var canceled = pairs
                .Select(pair => pair.Connection.ReceiveFrameValueAsync(cancellation.Token))
                .ToArray();
            Assert.All(canceled, receive => Assert.False(receive.IsCompleted));
            await QueueAndObservePrefixesAsync(pairs, canceledFrames);
            cancellation.Cancel();
            await Task.WhenAll(canceled.Select(ObserveCancellationAsync));

            Context.Value = "caller";
            var successors = pairs
                .Select(pair => pair.Connection.ReceiveFrameValueAsync())
                .ToArray();
            var completions = successors
                .Select((receive, index) => ObserveRawCompletion(receive, 499 + index))
                .ToArray();
            await Task.Run(async () =>
            {
                Context.Value = "producer";
                for (var index = 0; index < pairs.Count; index++)
                {
                    await pairs[index].QueueBytesAsync(
                        TcpReceiveTestPair.CreateFrame(messageId: 499 + index));
                }
            });
            await Task.WhenAll(completions).WaitAsync(Guard);
        }
        finally
        {
            Context.Value = previous;
            foreach (var pair in pairs)
            {
                await pair.DisposeAsync();
            }
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

    private static async Task QueueAndObservePrefixesAsync(
        IReadOnlyList<TcpReceiveTestPair> pairs,
        IReadOnlyList<byte[]> frames)
    {
        for (var index = 0; index < pairs.Count; index++)
        {
            await pairs[index].QueueBytesAsync(frames[index].AsMemory(0, sizeof(int)));
        }

        for (var index = 0; index < pairs.Count; index++)
        {
            await pairs[index].WaitForPrefixAsync(frames[index].Length);
        }
    }

    private static Task ObserveRawCompletion(ValueTask<RpcFrame> pending, int messageId)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable xUnit1030 // This regression intentionally observes the producer context.
        var awaiter = pending.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        awaiter.UnsafeOnCompleted(() =>
        {
            try
            {
                Assert.Equal("caller", Context.Value);
                using var frame = awaiter.GetResult();
                AssertFrame(frame, messageId);
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        });
        return completion.Task;
    }

    private static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }
}
