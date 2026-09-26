using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteEnumerable;

internal sealed class RemoteEnumerableLifetimeFixture
{
    public IAsyncEnumerable<Value>? Enumerable { get; private init; }
    public IAsyncEnumerator<Value>? Enumerator { get; private init; }
    public required WeakReference Sender { get; init; }
    public required WeakReference Receiver { get; init; }
    public required WeakReference Serializer { get; init; }
    public required WeakReference TokenSource { get; init; }
    public required State Tracking { get; init; }
    public Task<bool>? PendingRead { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RemoteEnumerableLifetimeFixture Create(string stage, string retain)
    {
        var state = new State { Fail = stage == "Failure" };
        var serializer = new TrackingSerializer(state);
        var sender = new SenderTarget(state);
        var manager = new RpcStreamManager(serializer, sender.SendAsync, exceptionTransformer: null);
        var receiver = manager.RegisterInboundResponse(new RpcStreamHandle(1, RpcStreamKind.Items), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var enumerable = new RpcRemoteAsyncEnumerable<Value>(receiver, serializer);
        var enumerator = stage == "Unstarted" ? null : enumerable.GetAsyncEnumerator(cancellation.Token);
        Task<bool>? pending = null;
        if (stage is "AfterRead" or "Completed" or "Exhausted" or "Failure" or "Live")
        {
            Assert.True(manager.TryAcceptItem(1, MessageFramer.FrameToPayload(1, MessageType.StreamItem, new byte[] { 1 })));
            if (stage == "Failure")
            {
                Assert.Throws<InvalidDataException>(() => enumerator!.MoveNextAsync().AsTask().GetAwaiter().GetResult());
            }
            else
            {
                Assert.True(enumerator!.MoveNextAsync().AsTask().GetAwaiter().GetResult());
                Assert.Equal(42, enumerator.Current.Number);
            }
        }

        if (stage is "Completed" or "Exhausted")
        {
            receiver.Complete();
            Assert.False(enumerator!.MoveNextAsync().AsTask().GetAwaiter().GetResult());
        }
        else if (stage == "Pending")
        {
            pending = enumerator!.MoveNextAsync().AsTask();
            Assert.False(pending.IsCompleted);
        }

        if (stage is not "Live" and not "Unstarted")
        {
            if (stage != "Exhausted")
            {
                enumerator!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            manager.Stop();
        }

        return new RemoteEnumerableLifetimeFixture
        {
            Enumerable = retain is "Enumerable" or "Both" ? enumerable : null,
            Enumerator = retain is "Enumerator" or "Both" ? enumerator : null,
            Sender = new WeakReference(sender),
            Receiver = new WeakReference(receiver),
            Serializer = new WeakReference(serializer),
            TokenSource = new WeakReference(cancellation),
            Tracking = state,
            PendingRead = pending,
        };
    }

    public async Task AssertCollected()
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!Sender.IsAlive && !Receiver.IsAlive && !Serializer.IsAlive && !TokenSource.IsAlive && Tracking.LastValue?.IsAlive is not true)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(Sender.IsAlive, "Completed enumeration must release its sender callback.");
        Assert.False(Receiver.IsAlive, "Completed enumeration must release its receiver.");
        Assert.False(Serializer.IsAlive, "Completed enumeration must release its serializer.");
        Assert.False(TokenSource.IsAlive, "Completed enumeration must release its cancellation token.");
        Assert.False(Tracking.LastValue?.IsAlive is true, "Completed enumeration must release the last yielded value.");
    }

    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public sealed record Value(int Number);

    public sealed class State
    {
        public bool Fail { get; init; }
        public WeakReference? LastValue { get; set; }
        public int Cancels;
    }

    private sealed class SenderTarget(State state)
    {
        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
            if (type == MessageType.StreamCancel)
            {
                Interlocked.Increment(ref state.Cancels);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TrackingSerializer(State state) : ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();

        public void Serialize<T>(IBufferWriter<byte> writer, T value) => _inner.Serialize(writer, value);

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            if (state.Fail)
            {
                throw new InvalidDataException("Cannot deserialize this item.");
            }

            var value = new Value(42);
            state.LastValue = new WeakReference(value);
            return (T)(object)value;
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
    }
}
