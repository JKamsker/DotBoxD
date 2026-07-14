using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcOutboundStreamErrorCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Async_enumerable_error_after_source_cancellation_does_not_send_stream_error()
    {
        var streamErrorSendTokens = new List<bool>();
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null,
            SendFrameAsync);
        var handle = streams.ReserveOutbound(RpcStreamKind.Items);
        using var callerCancellation = new CancellationTokenSource();
        await using var outbound = streams.RegisterOutbound(
            RpcStreamAttachment.FromAsyncEnumerable(
                handle,
                new CancelThenThrowAsyncEnumerable(callerCancellation)),
            callerCancellation.Token);

        outbound.Start();

        await outbound.WaitAsync().WaitAsync(Timeout);
        await WaitForOutboundCompletionAsync(streams);

        Assert.Empty(streamErrorSendTokens);

        ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken ct)
        {
            try
            {
                if (MessageFramer.TryReadFrameHeader(frame.WrittenMemory, out _, out var type) &&
                    type == MessageType.StreamError)
                {
                    streamErrorSendTokens.Add(ct.IsCancellationRequested);
                }

                return default;
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    private static async Task WaitForOutboundCompletionAsync(RpcStreamManager streams)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        while (streams.OutboundSenderCount != 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, streams.OutboundSenderCount);
    }

    private sealed class CancelThenThrowAsyncEnumerable : IAsyncEnumerable<int>
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelThenThrowAsyncEnumerable(CancellationTokenSource cancellation) =>
            _cancellation = cancellation;

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(_cancellation);

        private sealed class Enumerator : IAsyncEnumerator<int>
        {
            private readonly CancellationTokenSource _cancellation;

            public Enumerator(CancellationTokenSource cancellation) =>
                _cancellation = cancellation;

            public int Current => throw new InvalidOperationException("No item is produced.");

            public ValueTask DisposeAsync() => default;

            public ValueTask<bool> MoveNextAsync()
            {
                _cancellation.Cancel();
                throw new InvalidOperationException("Source failed after cancellation.");
            }
        }
    }
}
