using System.Buffers;
using System.Collections.Concurrent;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

public sealed class RpcStreamingContextLifecycleRegressionTests
{
    [Fact]
    public void DisabledContextCompletionRemainsStateless()
    {
        var context = RpcStreamingContext.Disabled;

        Assert.Null(context.CompleteDispatch());
        Assert.Null(context.CompleteDispatch());
        Assert.Throws<InvalidOperationException>(() => context.SetResponse(new MemoryStream()));
    }

    [Fact]
    public async Task SetResponseAfterDispatchCompletionFailsClosed()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var dispatcher = new CapturingDispatcher();
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        var builder = new RpcDispatchResponseBuilder(serializer, dispatchers);
        var context = new RpcStreamingContext(streams, serializer, CancellationToken.None);

        using var result = await builder.BuildAsync(
            new RpcRequest
            {
                MessageId = 1,
                ServiceName = dispatcher.ServiceName,
                MethodName = "Go",
            },
            messageId: 1,
            ReadOnlyMemory<byte>.Empty,
            new InstanceRegistry(),
            context,
            CancellationToken.None);

        Assert.Null(result.Stream);
        Assert.NotNull(dispatcher.CapturedStreaming);
        Assert.ThrowsAny<InvalidOperationException>(
            () => dispatcher.CapturedStreaming.SetResponse(new MemoryStream()));
        AssertNoPendingCreditForReleasedReservation(streams, streamId: 1);
    }

    private static void AssertNoPendingCreditForReleasedReservation(
        RpcStreamManager streams,
        int streamId)
    {
        using var credit = RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class CapturingDispatcher : IServiceDispatcher
    {
        public string ServiceName => "CaptureStreaming";

        public IRpcStreamingContext? CapturedStreaming { get; private set; }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            CapturedStreaming = streaming;
            return Task.CompletedTask;
        }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
