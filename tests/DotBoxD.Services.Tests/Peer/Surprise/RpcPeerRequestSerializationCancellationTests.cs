using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Streaming.Core;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.Surprise;

public sealed class RpcPeerRequestSerializationCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Request_serialization_cancellation_is_observed_before_send_delegate()
    {
        var serializer = new MessagePackRpcSerializer();
        var sender = new ObservingSender();
        var streams = new RpcStreamManager(serializer, sender.SendAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions
            {
                MaxPendingRequests = 1,
                RequestTimeout = Timeout,
            },
            ensureStarted: static () => { },
            sender.SendAsync,
            streams);

        Exception? firstFailure;
        int sendsAfterCancellation;
        Exception? secondFailure;
        using var cts = new CancellationTokenSource();

        try
        {
            firstFailure = await Record.ExceptionAsync(
                () => invoker
                    .InvokeAsync<CancelingRequest, int>(
                        "Service",
                        "Method",
                        new CancelingRequest(cts),
                        cts.Token)
                    .WaitAsync(Timeout));
            sendsAfterCancellation = sender.SendCalls;

            sender.ThrowMarkerOnNextSend();
            secondFailure = await Record.ExceptionAsync(
                () => invoker.InvokeAsync<int>("Service", "Method").WaitAsync(Timeout));
        }
        finally
        {
            await invoker.StopCancelFramesAsync();
            streams.Stop();
        }

        Assert.IsAssignableFrom<OperationCanceledException>(firstFailure);
        Assert.IsType<ExpectedSecondSendException>(secondFailure);
        Assert.Equal(0, sendsAfterCancellation);
    }

    [MessagePackObject]
    public sealed class CancelingRequest
    {
        public CancelingRequest()
        {
        }

        public CancelingRequest(CancellationTokenSource cts) =>
            Cancellation = cts;

        [IgnoreMember]
        public CancellationTokenSource? Cancellation { private get; set; }

        [Key(0)]
        public int Value
        {
            get
            {
                Cancellation?.Cancel();
                return 42;
            }

            set
            {
            }
        }
    }

    private sealed class ObservingSender
    {
        private int _sendCalls;
        private int _throwMarker;

        public int SendCalls => Volatile.Read(ref _sendCalls);

        public void ThrowMarkerOnNextSend() =>
            Volatile.Write(ref _throwMarker, 1);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _sendCalls);

            if (Volatile.Read(ref _throwMarker) != 0)
            {
                throw new ExpectedSecondSendException();
            }

            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ExpectedSecondSendException : Exception
    {
        public ExpectedSecondSendException()
            : base("second send reached")
        {
        }
    }
}
