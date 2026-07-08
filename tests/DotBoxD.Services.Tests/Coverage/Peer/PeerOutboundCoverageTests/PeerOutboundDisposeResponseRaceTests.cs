using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Peer.PeerOutboundTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed class PeerOutboundDisposeResponseRaceTests
{
    [Fact]
    public async Task InvokeAsync_DisposeStartedBeforeLateResponse_FaultsWithConnectionClosed()
    {
        var serializer = NewSerializer();
        await using var channel = new DisposeTriggeredResponseChannel(serializer, "late-success");
        await using var peer = RpcPeer.Over(channel, serializer, Options()).Start();

        var call = peer.InvokeAsync<int, string>(Service, Method, request: 1);
        await channel.WaitForRequestAsync(PeerOutboundTimeout);

        await peer.DisposeAsync().AsTask().WaitAsync(PeerOutboundTimeout);

        var ex = await Assert.ThrowsAsync<ServiceConnectionException>(
            () => call.WaitAsync(PeerOutboundTimeout));
        Assert.Contains("closed", ex.Message);
    }

    private sealed class DisposeTriggeredResponseChannel : IRpcChannel
    {
        private readonly MessagePackRpcSerializer _serializer;
        private readonly string _response;
        private readonly TaskCompletionSource<int> _requestId =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _responseReturned;

        public DisposeTriggeredResponseChannel(MessagePackRpcSerializer serializer, string response)
        {
            _serializer = serializer;
            _response = response;
        }

        public bool IsConnected => true;

        public string RemoteEndpoint => "race://late-response";

        public Task WaitForRequestAsync(TimeSpan timeout) => _requestId.Task.WaitAsync(timeout);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (MessageFramer.TryReadFrameHeader(data, out var messageId, out var messageType) &&
                messageType == MessageType.Request)
            {
                _requestId.TrySetResult(messageId);
            }

            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            await _disposeStarted.Task.ConfigureAwait(false);

            if (Interlocked.Exchange(ref _responseReturned, 1) != 0)
            {
                return Payload.Empty;
            }

            var messageId = await _requestId.Task.ConfigureAwait(false);
            return ResponseFrame(_serializer, messageId, _response);
        }

        public ValueTask DisposeAsync()
        {
            _disposeStarted.TrySetResult(true);
            return default;
        }
    }
}
