using System.Buffers;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer.FailureObservation;

internal static class RpcResponseFailureFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference Exercise(string kind, bool serialize, string marker)
    {
        var channel = new ControlledChannel();
        var expected = new IOException("Controlled request failure");
        var serializer = new FailingSerializer(expected);
        var peer = RpcPeer.Over(channel, serialize ? serializer : new MessagePackRpcSerializer(), new RpcPeerOptions
        {
            RequestTimeout = Timeout,
            EnableLowAllocationValueTaskInvocations = true,
        }).Start();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.Disconnected += (_, _) => disconnected.TrySetResult();
        var readError = new IOException(marker);
        void FailResponse()
        {
            channel.Receive.SetException(readError);
            disconnected.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
        }

        serializer.BeforeFailure = FailResponse;
        try
        {
            var actual = Record.Exception(() =>
            {
                var call = Invoke(peer, kind);
                if (!serialize)
                {
                    channel.Entered.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
                    FailResponse();
                    channel.Send.SetException(expected);
                }

                call.WaitAsync(Timeout).GetAwaiter().GetResult();
            });
            Assert.Same(expected, actual);
            Assert.Equal(!serialize, channel.Entered.Task.IsCompleted);
        }
        finally
        {
            peer.DisposeAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
        }

        return new WeakReference(readError);
    }

    private static Task Invoke(RpcPeer peer, string kind) => kind switch
    {
        "void" => peer.InvokeAsync("Service", "Method"),
        "void-request" => peer.InvokeAsync("Service", "Method", 123),
        "response" => peer.InvokeAsync<int>("Service", "Method"),
        "request-response" => peer.InvokeAsync<int, int>("Service", "Method", 123),
        "value-void" => peer.InvokeValueAsync("Service", "Method").AsTask(),
        "value-void-request" => peer.InvokeValueAsync("Service", "Method", 123).AsTask(),
        "value-response" => peer.InvokeValueAsync<int>("Service", "Method").AsTask(),
        "value-request-response" => peer.InvokeValueAsync<int, int>("Service", "Method", 123).AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class FailingSerializer(Exception error) : ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();
        public Action? BeforeFailure { get; set; }

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            BeforeFailure!();
            throw error;
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);
        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
    }

    private sealed class ControlledChannel : IRpcChannel
    {
        public TaskCompletionSource Send { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Payload> Receive { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected { get; private set; } = true;
        public string RemoteEndpoint => "memory://response-failure";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            return Send.Task;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) => Receive.Task.WaitAsync(ct);

        public Task CloseAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            Receive.TrySetResult(Payload.Empty);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(CloseAsync());
    }
}
