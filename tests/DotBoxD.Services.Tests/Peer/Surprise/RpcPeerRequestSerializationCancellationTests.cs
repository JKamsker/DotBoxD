using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
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

    [Fact]
    public async Task Request_serializer_fault_after_caller_cancellation_uses_cancellation_terminal_and_releases_pending_slot()
    {
        using var cts = new CancellationTokenSource();
        var serializer = new CancelingThenThrowingRequestSerializer(cts);
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

        try
        {
            var canceledFailure = await Record.ExceptionAsync(
                () => invoker
                    .InvokeAsync<int, int>("Service", "Method", request: 1, cts.Token)
                    .WaitAsync(Timeout));

            var cancellation = Assert.IsAssignableFrom<OperationCanceledException>(canceledFailure);
            Assert.Equal(cts.Token, cancellation.CancellationToken);
            Assert.Equal(1, serializer.RequestSerializationCalls);
            Assert.Equal(0, sender.SendCalls);

            serializer.CancelBeforeThrow = false;
            var ordinaryFailure = await Record.ExceptionAsync(
                () => invoker
                    .InvokeAsync<int, int>("Service", "Method", request: 2)
                    .WaitAsync(Timeout));

            Assert.IsType<ExpectedRequestSerializationException>(ordinaryFailure);
            Assert.Equal(2, serializer.RequestSerializationCalls);
            Assert.Equal(0, sender.SendCalls);
        }
        finally
        {
            await invoker.StopCancelFramesAsync();
            streams.Stop();
        }
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

    private sealed class CancelingThenThrowingRequestSerializer(CancellationTokenSource source) : ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();

        public int RequestSerializationCalls { get; private set; }

        public bool CancelBeforeThrow { get; set; } = true;

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (typeof(T) == typeof(int))
            {
                RequestSerializationCalls++;
                if (CancelBeforeThrow)
                {
                    source.Cancel();
                }

                throw new ExpectedRequestSerializationException();
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
    }

    private sealed class ExpectedRequestSerializationException : Exception
    {
        public ExpectedRequestSerializationException()
            : base("request serializer fault")
        {
        }
    }
}
