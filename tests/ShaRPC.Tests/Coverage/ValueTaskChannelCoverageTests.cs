using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests.Cov.Transport;

public sealed class ValueTaskChannelCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RpcPeer_UsesValueTaskChannelMethods_WhenAvailable()
    {
        await using var channel = new CountingValueTaskChannel();
        await using var peer = RpcPeer
            .Over(
                channel,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) })
            .Start();

        await channel.ReceiveCalled.Task.WaitAsync(Timeout);

        var call = peer.InvokeAsync<int>("Svc", "Op");
        await channel.SendCalled.Task.WaitAsync(Timeout);

        Assert.Equal(1, channel.SendValueCalls);
        Assert.Equal(0, channel.SendTaskCalls);
        Assert.Equal(1, channel.ReceiveValueCalls);
        Assert.Equal(0, channel.ReceiveTaskCalls);

        await peer.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => call.WaitAsync(Timeout));
    }

    private sealed class CountingValueTaskChannel : IRpcValueTaskChannel
    {
        private readonly TaskCompletionSource<Payload> _receive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => true;

        public string RemoteEndpoint => "valuetask://test";

        public int SendTaskCalls { get; private set; }

        public int SendValueCalls { get; private set; }

        public int ReceiveTaskCalls { get; private set; }

        public int ReceiveValueCalls { get; private set; }

        public TaskCompletionSource SendCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReceiveCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendTaskCalls++;
            return SendValueAsync(data, ct).AsTask();
        }

        public ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendValueCalls++;
            SendCalled.TrySetResult();
            return default;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ReceiveTaskCalls++;
            return ReceiveValueAsync(ct).AsTask();
        }

        public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default)
        {
            ReceiveValueCalls++;
            ReceiveCalled.TrySetResult();
            return new ValueTask<Payload>(_receive.Task);
        }

        public ValueTask DisposeAsync()
        {
            _receive.TrySetResult(Payload.Empty);
            return default;
        }
    }
}
