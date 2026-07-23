using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

[Collection(StreamReceiveOperationCollection.Name)]
public sealed class StreamFrameReceiveOperationIntegrationTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PendingPrefix_RestoresCallerExecutionContextBeforeBodyRead()
    {
        var previous = Context.Value;
        Context.Value = "caller";
        try
        {
            var stream = new ControlledPendingFrameStream(
                CreateFrames(1),
                static () => Context.Value);
            await using var connection = CreateConnection(stream);
            var pending = connection.ReceiveFrameValueAsync();

            await stream.WaitForReadAsync(Guard);
            Assert.Equal("caller", stream.GetObservedContext(0));
            await CompleteWithContextAsync(stream, "producer");

            await stream.WaitForReadAsync(Guard);
            Assert.Equal("caller", stream.GetObservedContext(1));
            await CompleteWithContextAsync(stream, "producer");

            using var frame = await pending.AsTask().WaitAsync(Guard);
            Assert.Equal(0, ReadMessageId(frame));
        }
        finally
        {
            Context.Value = previous;
        }
    }

    [Fact]
    public async Task InlineThrowingConsumer_CanStartSuccessorBeforeProducerUnwinds()
    {
        var stream = new ControlledPendingFrameStream(CreateFrames(2));
        await using var connection = CreateConnection(stream);
        var first = connection.ReceiveFrameValueAsync();
#pragma warning disable xUnit1030 // This regression intentionally registers a raw inline continuation.
        var awaiter = first.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        var marker = new InlineConsumerException();
        Exception? reentryError = null;
        ValueTask<RpcFrame> second = default;
        awaiter.UnsafeOnCompleted(() =>
        {
            try
            {
                using var frame = awaiter.GetResult();
                Assert.Equal(0, ReadMessageId(frame));
                second = connection.ReceiveFrameValueAsync();
            }
            catch (Exception error)
            {
                reentryError = error;
            }

            throw marker;
        });

        await stream.WaitForReadAsync(Guard);
        stream.CompleteNextRead();
        await stream.WaitForReadAsync(Guard);
        var thrown = Assert.Throws<InlineConsumerException>(stream.CompleteNextRead);

        Assert.Same(marker, thrown);
        Assert.Null(reentryError);
        Assert.False(second.IsCompleted);

        await stream.WaitForReadAsync(Guard);
        stream.CompleteNextRead();
        await stream.WaitForReadAsync(Guard);
        stream.CompleteNextRead();
        using var secondFrame = await second.AsTask().WaitAsync(Guard);
        Assert.Equal(1, ReadMessageId(secondFrame));
    }

    [Fact]
    public async Task CompletedUnconsumedValueTask_DoesNotRetainConnectionGraph()
    {
        var probe = CreateCompletedUnconsumedReceive();

        ForceGc();

        Assert.False(probe.Connection.IsAlive);
        Assert.False(probe.Stream.IsAlive);
        Assert.False(probe.CallerCancellation.IsAlive);
        using var frame = await probe.Pending;
        Assert.Equal(0, ReadMessageId(frame));
    }

    private static async Task CompleteWithContextAsync(
        ControlledPendingFrameStream stream,
        string value)
    {
        await Task.Run(() =>
        {
            Context.Value = value;
            stream.CompleteNextRead();
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetentionProbe CreateCompletedUnconsumedReceive()
    {
        var callerCancellation = new CancellationTokenSource();
        var stream = new ControlledPendingFrameStream(CreateFrames(1));
        var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: TimeSpan.FromMinutes(1));
        var pending = connection.ReceiveFrameValueAsync(callerCancellation.Token);

        stream.CompleteNextRead();
        stream.CompleteNextRead();
        if (!pending.IsCompleted)
        {
            throw new InvalidOperationException("The controlled frame did not complete inline.");
        }

        return new RetentionProbe(
            pending,
            new WeakReference(connection),
            new WeakReference(stream),
            new WeakReference(callerCancellation));
    }

    private static StreamConnection CreateConnection(Stream stream) =>
        new(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);

    private static int ReadMessageId(RpcFrame frame)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out _));
        return messageId;
    }

    private static byte[] CreateFrames(int count)
    {
        using var first = MessageFramer.FrameToPayload(
            0,
            MessageType.Response,
            new byte[] { 1, 2, 3 });
        var frameLength = first.Length;
        var result = new byte[frameLength * count];
        first.Memory.CopyTo(result);
        for (var index = 1; index < count; index++)
        {
            using var frame = MessageFramer.FrameToPayload(
                index,
                MessageType.Response,
                new byte[] { 1, 2, 3 });
            frame.Memory.CopyTo(result.AsMemory(index * frameLength));
        }

        return result;
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
        WeakReference Stream,
        WeakReference CallerCancellation);

    private sealed class InlineConsumerException : Exception;
}
