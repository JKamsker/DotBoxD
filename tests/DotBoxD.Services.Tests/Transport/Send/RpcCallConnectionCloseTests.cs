using System.IO.Pipes;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Transport.Send.TcpPooling;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

// A call whose request write is in flight when its connection closes lost the connection; its caller
// never cancelled it. It must fail with a connection error, never OperationCanceledException (a
// TaskCanceledException from the call), which callers and retry policies treat as deliberate
// cancellation. The remote never reads, so the large request stalls in the transport's write.
public sealed class RpcCallConnectionCloseTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StallDelay = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task NamedPipeConnectionClose_FailsInFlightCallAsConnectionLoss()
    {
        var pipeName = $"dotboxd-rpc-close-{Guid.NewGuid():N}";
        await using var remote = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var local = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepting = remote.WaitForConnectionAsync();
        await local.ConnectAsync().WaitAsync(Guard);
        await accepting.WaitAsync(Guard);

        var connection = new StreamConnection(local, ownsStream: true);
        var errors = await CloseWithCallsInFlightAsync(connection, payloadLength: 4 * 1024 * 1024);

        foreach (var error in errors)
        {
            var disposed = Assert.IsType<ObjectDisposedException>(error);
            Assert.Equal(nameof(StreamConnection), disposed.ObjectName);
        }
    }

    [Fact]
    public async Task TcpConnectionClose_FailsInFlightCallAsConnectionLoss()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var errors = await CloseWithCallsInFlightAsync(pair.Connection, payloadLength: 12 * 1024 * 1024);

        Assert.All(errors, static error => Assert.True(
            error is IOException or ObjectDisposedException,
            $"Expected a connection failure, got {error.GetType()}."));
    }

    private static async Task<Exception[]> CloseWithCallsInFlightAsync(IRpcChannel connection, int payloadLength)
    {
        var client = RpcPeer.Over(
                connection,
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RejectInboundCalls = true })
            .Start();
        try
        {
            var payload = new byte[payloadLength];

            // The first call's write stalls on backpressure; the second waits behind it on the send gate.
            var calls = Enumerable.Range(0, 3)
                .Select(_ => client.InvokeAsync("Remote", "Upload", payload))
                .ToArray();
            await Task.Delay(StallDelay);
            Assert.All(calls, static call => Assert.False(call.IsCompleted));

            await connection.DisposeAsync().AsTask().WaitAsync(Guard);

            var errors = new Exception[calls.Length];
            for (var index = 0; index < calls.Length; index++)
            {
                var error = await Record.ExceptionAsync(() => calls[index].WaitAsync(Guard));
                Assert.NotNull(error);
                Assert.IsNotType<TimeoutException>(error);
                Assert.False(
                    error is OperationCanceledException,
                    $"A lost connection surfaced as cancellation: {error}");
                Assert.IsNotType<ServiceTimeoutException>(error);
                errors[index] = error;
            }

            return errors;
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
