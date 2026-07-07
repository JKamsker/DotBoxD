using System.Buffers;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingResponseStreamCancelRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResponseStreamStartGracePeriod = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task EarlyStreamCancelBeforeResponseSenderRegistration_ReleasesRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcStreamManager? streams = null;
        var responseSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        streams = new RpcStreamManager(serializer, SendAndCancelResponseStreamAsync, exceptionTransformer: null);
        var dispatcher = new CanceledResponseDispatcher();
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions { InboundQueueCapacity = null },
            streams,
            SendAndCancelResponseStreamAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });
        inbound.AddDispatcher(dispatcher);
        var request = MessageFramer.FrameMessage(
            serializer,
            41,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 41,
                ServiceName = dispatcher.ServiceName,
                MethodName = "Go",
            },
            ReadOnlySpan<byte>.Empty);

        Assert.True(await inbound.AcceptRequestAsync(request, 41, CancellationToken.None));
        await responseSent.Task.WaitAsync(Timeout);
        await WaitUntilAsync(() => inbound.ActiveInboundCount == 0);

        Assert.Equal(0, streams!.OutboundSenderCount);
        Assert.False(dispatcher.ProducerStarted.IsCompleted);

        Task SendAndCancelResponseStreamAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrame(
                frame,
                out _,
                out var type,
                out var envelope,
                out _));
            if (type == MessageType.Response)
            {
                var response = serializer.Deserialize<RpcResponse>(envelope);
                if (response.Stream is not { } handle)
                {
                    throw new InvalidOperationException("Expected a streamed response.");
                }

                streams!.CancelOutbound(handle.StreamId);
                responseSent.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RequestCancelBeforeResponseSenderRegistration_DoesNotStartResponseSource()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcStreamManager? streams = null;
        RpcPeerInboundDispatcher? inbound = null;
        const int MessageId = 43;
        var responseSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        streams = new RpcStreamManager(serializer, SendAndCancelInboundAsync, exceptionTransformer: null);
        var dispatcher = new CancelInboundResponseDispatcher();
        inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions { InboundQueueCapacity = null },
            streams,
            SendAndCancelInboundAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });
        inbound.AddDispatcher(dispatcher);
        var request = MessageFramer.FrameMessage(
            serializer,
            MessageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = MessageId,
                ServiceName = dispatcher.ServiceName,
                MethodName = "Go",
            },
            ReadOnlySpan<byte>.Empty);

        Assert.True(await inbound.AcceptRequestAsync(request, MessageId, CancellationToken.None));
        await responseSent.Task.WaitAsync(Timeout);
        await WaitUntilAsync(() => inbound.ActiveInboundCount == 0);

        var sourceStarted = await SourceStartedWithinAsync(dispatcher.ResponseStream.ReadStarted);
        Assert.False(sourceStarted);
        Assert.Equal(0, streams!.OutboundSenderCount);

        Task SendAndCancelInboundAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrame(
                frame,
                out _,
                out var type,
                out var envelope,
                out _));
            if (type == MessageType.Response)
            {
                var response = serializer.Deserialize<RpcResponse>(envelope);
                if (response.Stream is null)
                {
                    throw new InvalidOperationException("Expected a streamed response.");
                }

                inbound!.Cancel(MessageId);
                responseSent.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!predicate())
        {
            await Task.Delay(10, cts.Token).ConfigureAwait(false);
        }
    }

    private static async Task<bool> SourceStartedWithinAsync(Task readStarted)
    {
        var completed = await Task.WhenAny(
            readStarted,
            Task.Delay(ResponseStreamStartGracePeriod)).ConfigureAwait(false);
        return completed == readStarted;
    }

    private sealed class CanceledResponseDispatcher : IServiceDispatcher
    {
        private readonly TaskCompletionSource _producerStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => "CanceledResponse";

        public Task ProducerStarted => _producerStarted.Task;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            streaming.SetResponse(ItemsAsync());
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

        private async IAsyncEnumerable<int> ItemsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _producerStarted.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            yield return 1;
        }
    }

    private sealed class CancelInboundResponseDispatcher : IServiceDispatcher
    {
        public string ServiceName => "CancelInboundResponse";

        public ReadTrackingStream ResponseStream { get; } = new();

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            streaming.SetResponse(ResponseStream);
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

    private sealed class ReadTrackingStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<int>(cancellationToken)
                : new ValueTask<int>(0);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
