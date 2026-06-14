using System.Buffers;
using DotBoxD.Services;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Codecs.MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests;

public sealed class RemoteCancellationTests
{
    [Fact]
    public async Task CallerCancellation_CancelsInFlightRemoteDispatch()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        var service = new CancellableService();

        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Provide((IServiceDispatcher)service)
            .Start();

        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        using var requestCts = new CancellationTokenSource();

        // Cancelling the caller's token must emit a Cancel frame that cancels the remote dispatch.
        var call = client.InvokeAsync(service.ServiceName, "Wait", requestCts.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        requestCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(30)));
        await service.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class CancellableService : IServiceDispatcher
    {
        public string ServiceName => "CancelAware";

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            if (method != "Wait")
            {
                throw new InvalidOperationException("Unexpected method: " + method);
            }

            Started.TrySetResult();

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
