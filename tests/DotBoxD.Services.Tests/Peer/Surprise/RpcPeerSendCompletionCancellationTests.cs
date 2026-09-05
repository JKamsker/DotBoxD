using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
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
}
