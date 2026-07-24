using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

[Collection(StreamReceiveOperationCollection.Name)]
public sealed class StreamDedicatedReceiveOperationSemanticsTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CleanEndOfStream_DoesNotAdmitCache()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        var pending = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(pending.IsCompleted);

        pair.Peer.Dispose();
        var frame = await pending.AsTask().WaitAsync(Guard);
        using var payload = frame.DetachPayload();
        Assert.Same(Payload.Empty, payload);
        Assert.False(pair.Connection.HasDedicatedReceiveCache);
    }

    [Fact]
    public async Task PendingCallerCancellation_DoesNotAdmitCache()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        using var cancellation = new CancellationTokenSource();
        var pending = pair.Connection.ReceiveFrameValueAsync(cancellation.Token);
        Assert.False(pending.IsCompleted);

        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(pair.Connection.HasDedicatedReceiveCache);
    }

    [Fact]
    public async Task IdleTimeout_DoesNotAdmitCache()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync(
            overflowIdleTimeout: TimeSpan.FromMilliseconds(100));
        var pair = population.OverflowPair;
        var pending = pair.Connection.ReceiveFrameValueAsync();

        var error = await Assert.ThrowsAsync<IOException>(
            () => pending.AsTask().WaitAsync(Guard));
        Assert.Contains("stalled", error.Message);
        Assert.False(pair.Connection.HasDedicatedReceiveCache);
    }

    [Fact]
    public async Task InvalidAndTruncatedFrames_DoNotAdmitCache()
    {
        await using (var invalidPopulation = await StreamSaturatedReceivePopulation.CreateAsync())
        {
            var pair = invalidPopulation.OverflowPair;
            var pending = pair.Connection.ReceiveFrameValueAsync();
            var prefix = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, 1);
            await pair.QueueBytesAsync(prefix);
            await Assert.ThrowsAsync<InvalidDataException>(() => pending.AsTask().WaitAsync(Guard));
            Assert.False(pair.Connection.HasDedicatedReceiveCache);
        }

        await using var truncatedPopulation = await StreamSaturatedReceivePopulation.CreateAsync();
        var truncatedPair = truncatedPopulation.OverflowPair;
        var truncated = truncatedPair.Connection.ReceiveFrameValueAsync();
        var frame = StreamReceiveTestPair.CreateFrame(messageId: 941);
        await truncatedPair.QueueBytesAsync(frame.AsMemory(0, sizeof(int) + 1));
        truncatedPair.Peer.Dispose();
        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => truncated.AsTask().WaitAsync(Guard));
        Assert.Equal($"Connection closed after 5 of {frame.Length} frame bytes.", error.Message);
        Assert.False(truncatedPair.Connection.HasDedicatedReceiveCache);
    }

    [Fact]
    public async Task DedicatedCompletion_RestoresCallerExecutionContext()
    {
        await using var population = await StreamSaturatedReceivePopulation.CreateAsync();
        var pair = population.OverflowPair;
        await StreamDedicatedReceiveOperationTests.AdmitCacheAsync(pair, messageId: 950);
        var previous = Context.Value;
        try
        {
            Context.Value = "caller";
            var pending = pair.Connection.ReceiveFrameValueAsync();
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
                    StreamDedicatedReceiveOperationTests.AssertFrame(frame, messageId: 951);
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            });

            await Task.Run(async () =>
            {
                Context.Value = "producer";
                await pair.QueueBytesAsync(StreamReceiveTestPair.CreateFrame(messageId: 951));
            });
            await completion.Task.WaitAsync(Guard);
        }
        finally
        {
            Context.Value = previous;
        }
    }
}
