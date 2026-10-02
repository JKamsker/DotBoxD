using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerSendCompletionCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Unary_response_completed_during_send_is_canceled_when_send_cancels_caller()
    {
        var serializer = new MessagePackRpcSerializer();
        using var cts = new CancellationTokenSource();
        RpcPeerOutboundInvoker? invoker = null;
        var sendCalls = 0;
        var streams = new RpcStreamManager(serializer, SendAsync, exceptionTransformer: null);

        try
        {
            invoker = new RpcPeerOutboundInvoker(
                serializer,
                new RpcPeerOptions
                {
                    MaxPendingRequests = 1,
                    RequestTimeout = Timeout,
                },
                ensureStarted: static () => { },
                SendAsync,
                streams);

            var failure = await Record.ExceptionAsync(
                () => invoker
                    .InvokeAsync<int, int>("Service", "Method", request: 1, cts.Token)
                    .WaitAsync(Timeout));

            var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(failure);
            Assert.Equal(cts.Token, cancellation.CancellationToken);
            Assert.True(cts.IsCancellationRequested);

            Assert.Equal(
                456,
                await invoker.InvokeAsync<int, int>("Service", "Method", request: 2).WaitAsync(Timeout));
        }
        finally
        {
            if (invoker is not null)
            {
                await invoker.StopCancelFramesAsync();
            }

            streams.Stop();
        }

        Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!MessageFramer.TryReadFrameHeader(data, out var messageId, out var messageType) ||
                messageType != MessageType.Request)
            {
                return Task.CompletedTask;
            }

            var responseValue = Interlocked.Increment(ref sendCalls) == 1 ? 123 : 456;
            using var payload = serializer.SerializeToPayload(responseValue);
            var response = MessageFramer.FrameMessage(
                serializer,
                messageId,
                MessageType.Response,
                new RpcResponse { MessageId = messageId, IsSuccess = true },
                payload.Memory.Span);
            if (!invoker!.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
            }

            if (responseValue == 123)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Channel_send_fault_after_caller_cancellation_uses_cancellation_terminal_and_releases_pending_slot()
    {
        using var cts = new CancellationTokenSource();
        var channel = new CancelThenFaultChannel(cts);
        await using var peer = RpcPeer.Over(
            channel,
            new MessagePackRpcSerializer(),
            new RpcPeerOptions
            {
                MaxPendingRequests = 1,
                RequestTimeout = Timeout,
            });

        var firstFailure = await Record.ExceptionAsync(
            () => peer
                .InvokeAsync<int, int>("Service", "Method", request: 1, cts.Token)
                .WaitAsync(Timeout));

        var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(firstFailure);
        Assert.Equal(cts.Token, cancellation.CancellationToken);
        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(1, channel.SendCalls);

        var secondFailure = await Record.ExceptionAsync(
            () => peer
                .InvokeAsync<int, int>("Service", "Method", request: 2)
                .WaitAsync(Timeout));

        Assert.IsType<InvalidOperationException>(secondFailure);
        Assert.Equal(2, channel.SendCalls);
    }

    private sealed class CancelThenFaultChannel(CancellationTokenSource callerCancellation) : IRpcChannel
    {
        private int _sendCalls;

        public int SendCalls => Volatile.Read(ref _sendCalls);

        public bool IsConnected => true;

        public string RemoteEndpoint => "test://channel-send-fault";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _sendCalls) == 1)
            {
                callerCancellation.Cancel();
            }

            return Task.FromException(new InvalidOperationException("channel send failed"));
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return Payload.Empty;
        }

        public ValueTask DisposeAsync() => default;
    }
}
