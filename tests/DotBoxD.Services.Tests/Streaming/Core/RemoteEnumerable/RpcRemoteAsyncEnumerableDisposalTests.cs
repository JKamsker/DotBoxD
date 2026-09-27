using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteEnumerable;

public sealed class RpcRemoteAsyncEnumerableDisposalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_during_deserialization_does_not_restore_current_or_release_credit(bool concurrent)
    {
        var serializer = new CallbackSerializer();
        var sent = new List<MessageType>();
        var (manager, enumerable) = Create(serializer, sent);
        var enumerator = enumerable.GetAsyncEnumerator();
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        serializer.OnDeserialize = () =>
        {
            if (concurrent)
            {
                entered.Set();
                Assert.True(proceed.Wait(TimeSpan.FromSeconds(5)));
            }
            else
            {
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }
        };
        Assert.True(manager.TryAcceptItem(1, MessageFramer.FrameToPayload(1, MessageType.StreamItem, new byte[] { 1 })));

        var move = Task.Run(() => enumerator.MoveNextAsync().AsTask());
        if (concurrent)
        {
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                await enumerator.DisposeAsync();
            }
            finally
            {
                proceed.Set();
            }
        }

        Assert.False(await move.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(enumerator.Current);
        await enumerator.DisposeAsync();
        Assert.Equal(new[] { MessageType.StreamCredit, MessageType.StreamCancel }, sent);
    }

    [Fact]
    public async Task Deserializer_failures_preserve_the_next_item_until_disposal()
    {
        var expected = new InvalidDataException("Invalid item.");
        var serializer = new CallbackSerializer { OnDeserialize = () => throw expected };
        var sent = new List<MessageType>();
        var (manager, enumerable) = Create(serializer, sent);
        await using var enumerator = enumerable.GetAsyncEnumerator();
        Assert.True(manager.TryAcceptItem(1, MessageFramer.FrameToPayload(1, MessageType.StreamItem, new byte[] { 1 })));
        Assert.True(manager.TryAcceptItem(1, MessageFramer.FrameToPayload(1, MessageType.StreamItem, new byte[] { 2 })));

        Assert.Same(expected, await Record.ExceptionAsync(() => enumerator.MoveNextAsync().AsTask()));
        serializer.OnDeserialize = null;
        Assert.True(await enumerator.MoveNextAsync());
        Assert.NotNull(enumerator.Current);
        Assert.Equal(3, sent.Count(type => type == MessageType.StreamCredit));
        Assert.DoesNotContain(MessageType.StreamCancel, sent);
    }

    [Fact]
    public async Task Concurrent_enumerator_creation_transfers_ownership_once()
    {
        var sent = new List<MessageType>();
        var (_, enumerable) = Create(new CallbackSerializer(), sent);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            try
            {
                return enumerable.GetAsyncEnumerator();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        })));

        var winner = Assert.Single(attempts, item => item is not null);
        await winner!.DisposeAsync();
        await winner.DisposeAsync();
        Assert.Equal(new[] { MessageType.StreamCredit, MessageType.StreamCancel }, sent);
    }

    private static (RpcStreamManager Manager, IAsyncEnumerable<object> Enumerable) Create(ISerializer serializer, List<MessageType> sent)
    {
        Task Send(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
            lock (sent)
            {
                sent.Add(type);
            }

            return Task.CompletedTask;
        }

        var manager = new RpcStreamManager(serializer, Send, exceptionTransformer: null);
        var receiver = manager.RegisterInboundResponse(new RpcStreamHandle(1, RpcStreamKind.Items), CancellationToken.None);
        return (manager, new RpcRemoteAsyncEnumerable<object>(receiver, serializer));
    }

    private sealed class CallbackSerializer : ISerializer
    {
        private readonly MessagePackRpcSerializer _inner = new();
        public Action? OnDeserialize { get; set; }
        public void Serialize<T>(IBufferWriter<byte> writer, T value) => _inner.Serialize(writer, value);
        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            OnDeserialize?.Invoke();
            return (T)new object();
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
    }
}
