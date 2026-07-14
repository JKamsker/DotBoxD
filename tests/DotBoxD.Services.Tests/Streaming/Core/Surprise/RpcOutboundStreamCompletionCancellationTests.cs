using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcOutboundStreamCompletionCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Async_enumerable_completion_cancellation_is_observed_before_stream_complete_send()
    {
        var streamCompleteSendTokens = new List<bool>();
        var streams = new RpcStreamManager(
            new MessagePackRpcSerializer(),
            (_, _) => Task.CompletedTask,
            exceptionTransformer: null,
            SendFrameAsync);
        var handle = streams.ReserveOutbound(RpcStreamKind.Items);
        using var callerCancellation = new CancellationTokenSource();
        var attachment = RpcStreamAttachment.FromAsyncEnumerable(
            handle,
            new CancelingEmptyAsyncEnumerable(callerCancellation));

        await using var outbound = streams.RegisterOutbound(attachment, callerCancellation.Token);
        outbound.Start();

        await outbound.WaitAsync().WaitAsync(Timeout);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Empty(streamCompleteSendTokens);

        ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken ct)
        {
            try
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame.WrittenMemory, out _, out var type));
                if (type == MessageType.StreamComplete)
                {
                    streamCompleteSendTokens.Add(ct.IsCancellationRequested);
                }

                return default;
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    private sealed class CancelingEmptyAsyncEnumerable(CancellationTokenSource callerCancellation) :
        IAsyncEnumerable<int>,
        IAsyncEnumerator<int>
    {
        private int _moved;

        public int Current => 0;

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            if (Interlocked.Exchange(ref _moved, 1) != 0)
            {
                return new ValueTask<bool>(false);
            }

            callerCancellation.Cancel();
            return new ValueTask<bool>(false);
        }

        public ValueTask DisposeAsync() => default;
    }
}
