using System.Buffers;
using System.Collections.Concurrent;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamedResponseSerializationCancellationRegressionTests
{
    [Fact]
    public async Task BuildAsync_AbandonsStreamedResponseWhenFinalEnvelopeSerializationCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var serializer = new CancelingStreamedResponseSerializer(
            new MessagePackRpcSerializer(),
            cancellation);
        var streams = CreateStreamManager(serializer);
        var dispatcher = new SetResponseDispatcher();
        var builder = CreateBuilder(serializer, dispatcher);
        var context = new RpcStreamingContext(streams, serializer, CancellationToken.None);
        RpcDispatchResult? result = null;

        try
        {
            var exception = await Record.ExceptionAsync(async () =>
            {
                result = await builder.BuildAsync(
                    CreateRequest(dispatcher.ServiceName),
                    messageId: 1,
                    ReadOnlyMemory<byte>.Empty,
                    new InstanceRegistry(),
                    context,
                    cancellation.Token);
            });

            var failures = new List<string>();
            if (exception is not OperationCanceledException)
            {
                failures.Add($"Expected OperationCanceledException, got {exception?.GetType().Name ?? "no exception"}.");
            }

            if (result is not null)
            {
                failures.Add("BuildAsync returned a streamed response result after cancellation.");
            }

            if (!serializer.CanceledDuringStreamedResponse)
            {
                failures.Add("The serializer did not cancel during streamed response envelope serialization.");
            }

            if (!dispatcher.ResponseStream.Disposed)
            {
                failures.Add("The streamed response source was not abandoned and disposed.");
            }

            if (!ReservationWasReleased(streams, streamId: 1))
            {
                failures.Add("The outbound streamed response reservation was not released.");
            }

            Assert.Empty(failures);
        }
        finally
        {
            result?.Dispose();
        }
    }

    private static RpcDispatchResponseBuilder CreateBuilder(
        ISerializer serializer,
        IServiceDispatcher dispatcher)
    {
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        return new RpcDispatchResponseBuilder(serializer, dispatchers);
    }

    private static RpcRequest CreateRequest(string serviceName) =>
        new()
        {
            MessageId = 1,
            ServiceName = serviceName,
            MethodName = "Go",
        };

    private static RpcStreamManager CreateStreamManager(ISerializer serializer) =>
        new(serializer, SendNoopAsync, exceptionTransformer: null);

    private static bool ReservationWasReleased(RpcStreamManager streams, int streamId)
    {
        using var credit = RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, 1);
        return streams.TryAddCredit(credit) && streams.PendingCreditCount == 0;
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class SetResponseDispatcher : IServiceDispatcher
    {
        public string ServiceName => "SetResponse";

        public TrackingStream ResponseStream { get; } = new();

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

    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            Disposed = true;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class CancelingStreamedResponseSerializer : ISerializer
    {
        private readonly ISerializer _inner;
        private readonly CancellationTokenSource _cancellation;

        public CancelingStreamedResponseSerializer(
            ISerializer inner,
            CancellationTokenSource cancellation)
        {
            _inner = inner;
            _cancellation = cancellation;
        }

        public bool CanceledDuringStreamedResponse { get; private set; }

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (value is RpcResponse { IsSuccess: true, Stream: not null })
            {
                CanceledDuringStreamedResponse = true;
                _cancellation.Cancel();
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) =>
            _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            _inner.Deserialize(data, type);
    }
}
