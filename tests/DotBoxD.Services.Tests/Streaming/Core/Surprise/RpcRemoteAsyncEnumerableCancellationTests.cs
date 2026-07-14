using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcRemoteAsyncEnumerableCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task MoveNextAsync_WhenDeserializeCancelsEnumerationToken_DoesNotYieldOrReleaseCredit()
    {
        var creditFrameCount = 0;

        Task CountCreditFramesAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) &&
                type == MessageType.StreamCredit)
            {
                Interlocked.Increment(ref creditFrameCount);
            }

            return Task.CompletedTask;
        }

        using var cts = new CancellationTokenSource();
        var serializer = new CancelingDeserializer(cts, value: 42);
        var streams = new RpcStreamManager(serializer, CountCreditFramesAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(91_001, RpcStreamKind.Items);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        var baselineCreditFrameCount = creditFrameCount;

        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0x2A });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        var enumerable = new RpcRemoteAsyncEnumerable<int>(receiver, serializer);
        await using var enumerator = enumerable.GetAsyncEnumerator(cts.Token);

        bool? moved = null;
        var moveNextException = await Record.ExceptionAsync(async () =>
            moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));

        var extraCreditFrameCount = creditFrameCount - baselineCreditFrameCount;
        Assert.True(
            moveNextException is OperationCanceledException,
            "Expected deserialization cancellation to win before yielding. " +
            $"MoveNextException={moveNextException?.GetType().Name ?? "<null>"}, " +
            $"MoveNextResult={moved?.ToString() ?? "<none>"}, " +
            $"Current={enumerator.Current}, " +
            $"ExtraCredits={extraCreditFrameCount}.");
        Assert.Equal(baselineCreditFrameCount, creditFrameCount);
    }

    private sealed class CancelingDeserializer : ISerializer
    {
        private readonly CancellationTokenSource _cts;
        private readonly MessagePackRpcSerializer _inner = new();
        private readonly int _value;

        public CancelingDeserializer(CancellationTokenSource cts, int value)
        {
            _cts = cts;
            _value = value;
        }

        public void Serialize<T>(IBufferWriter<byte> writer, T value) =>
            _inner.Serialize(writer, value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            _cts.Cancel();
            return (T)(object)_value;
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
            type == typeof(int) ? _value : _inner.Deserialize(data, type);
    }
}
