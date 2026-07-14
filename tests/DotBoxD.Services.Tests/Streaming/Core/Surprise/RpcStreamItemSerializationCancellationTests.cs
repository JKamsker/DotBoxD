using System.Buffers;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamItemSerializationCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Async_enumerable_item_serialization_cancellation_is_observed_before_frame_send()
    {
        var sendEntries = 0;
        using var callerCancellation = new CancellationTokenSource();
        var serializer = new CancelingSerializer(callerCancellation);
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null,
            SendFrameAsync);
        var handle = streams.ReserveOutbound(RpcStreamKind.Items);
        var item = new StreamItem();
        var attachment = RpcStreamAttachment.FromAsyncEnumerable(
            handle,
            new SingleItemAsyncEnumerable(item));
        await using var outbound = streams.RegisterOutbound(attachment, callerCancellation.Token);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);

        Assert.True(streams.TryAddCredit(credit));
        outbound.Start();
        await outbound.WaitAsync().WaitAsync(Timeout);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Equal(0, sendEntries);

        ValueTask SendFrameAsync(PooledBufferWriter frame, CancellationToken ct)
        {
            try
            {
                Interlocked.Increment(ref sendEntries);
                ct.ThrowIfCancellationRequested();
                return default;
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    private sealed class CancelingSerializer(CancellationTokenSource cancellation) : ISerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            cancellation.Cancel();
            var span = writer.GetSpan(1);
            span[0] = 42;
            writer.Advance(1);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) =>
            throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            throw new NotSupportedException();
    }

    private sealed class StreamItem;

    private sealed class SingleItemAsyncEnumerable(StreamItem item) :
        IAsyncEnumerable<StreamItem>
    {
        public IAsyncEnumerator<StreamItem> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) =>
            new SingleItemAsyncEnumerator(item, cancellationToken);
    }

    private sealed class SingleItemAsyncEnumerator(
        StreamItem item,
        CancellationToken cancellationToken) : IAsyncEnumerator<StreamItem>
    {
        private int _moved;

        public StreamItem Current { get; private set; } = null!;

        public ValueTask DisposeAsync() => default;

        public ValueTask<bool> MoveNextAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _moved, 1) != 0)
            {
                return new ValueTask<bool>(false);
            }

            Current = item;
            return new ValueTask<bool>(true);
        }
    }
}
