using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

public sealed class RpcStreamingContextHandleValidationContractTests
{
    private const int DeclaredStreamId = 30_001;

    [Theory]
    [InlineData(StreamingContextAccessor.Stream, 0, (int)RpcStreamKind.Binary)]
    [InlineData(StreamingContextAccessor.Stream, -1, (int)RpcStreamKind.Binary)]
    [InlineData(StreamingContextAccessor.Stream, DeclaredStreamId, 99)]
    [InlineData(StreamingContextAccessor.Pipe, 0, (int)RpcStreamKind.Binary)]
    [InlineData(StreamingContextAccessor.Pipe, -1, (int)RpcStreamKind.Binary)]
    [InlineData(StreamingContextAccessor.Pipe, DeclaredStreamId, 99)]
    [InlineData(StreamingContextAccessor.AsyncEnumerable, 0, (int)RpcStreamKind.Items)]
    [InlineData(StreamingContextAccessor.AsyncEnumerable, -1, (int)RpcStreamKind.Items)]
    [InlineData(StreamingContextAccessor.AsyncEnumerable, DeclaredStreamId, 99)]
    public void AccessorsRejectMalformedHandlesAtPublicBoundary(
        StreamingContextAccessor accessor,
        int streamId,
        int kind)
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var declared = new RpcStreamHandle(DeclaredStreamId, ExpectedKind(accessor));
        streams.RegisterInbound(new[] { declared }, CancellationToken.None);
        var context = new RpcStreamingContext(
            streams,
            serializer,
            CancellationToken.None,
            new[] { declared });
        var malformed = new RpcStreamHandle(streamId, (RpcStreamKind)kind);

        var error = Assert.IsAssignableFrom<ArgumentException>(
            Assert.ThrowsAny<Exception>(() => UseAccessor(context, accessor, malformed)));

        Assert.Equal("handle", error.ParamName);
        Assert.Equal(1, streams.InboundReceiverCount);
        var unclaimed = Assert.Throws<ServiceProtocolException>(
            context.EnsureAllDeclaredInboundStreamsClaimed);
        Assert.Contains("was not claimed", unclaimed.Message);
        streams.RemoveInbound(declared.StreamId);
    }

    [Fact]
    public void AccessorKeepsProtocolErrorForWellFormedUndeclaredHandle()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var declared = new RpcStreamHandle(DeclaredStreamId, RpcStreamKind.Binary);
        streams.RegisterInbound(new[] { declared }, CancellationToken.None);
        var context = new RpcStreamingContext(
            streams,
            serializer,
            CancellationToken.None,
            new[] { declared });

        var error = Assert.Throws<ServiceProtocolException>(
            () => context.GetStream(new RpcStreamHandle(DeclaredStreamId + 1, RpcStreamKind.Binary)));

        Assert.Contains("was not declared", error.Message);
        streams.RemoveInbound(declared.StreamId);
    }

    [Fact]
    public void AccessorKeepsProtocolErrorForWellFormedWrongKindHandle()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = CreateStreamManager(serializer);
        var declared = new RpcStreamHandle(DeclaredStreamId, RpcStreamKind.Binary);
        streams.RegisterInbound(new[] { declared }, CancellationToken.None);
        var context = new RpcStreamingContext(
            streams,
            serializer,
            CancellationToken.None,
            new[] { declared });

        var error = Assert.Throws<ServiceProtocolException>(
            () => context.GetAsyncEnumerable<int>(declared));

        Assert.Contains("not 'Items'", error.Message);
        streams.RemoveInbound(declared.StreamId);
    }

    private static RpcStreamKind ExpectedKind(StreamingContextAccessor accessor) =>
        accessor == StreamingContextAccessor.AsyncEnumerable
            ? RpcStreamKind.Items
            : RpcStreamKind.Binary;

    private static void UseAccessor(
        RpcStreamingContext context,
        StreamingContextAccessor accessor,
        RpcStreamHandle handle)
    {
        switch (accessor)
        {
            case StreamingContextAccessor.Stream:
                _ = context.GetStream(handle);
                break;
            case StreamingContextAccessor.Pipe:
                _ = context.GetPipe(handle);
                break;
            case StreamingContextAccessor.AsyncEnumerable:
                _ = context.GetAsyncEnumerable<int>(handle);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(accessor), accessor, null);
        }
    }

    private static RpcStreamManager CreateStreamManager(MessagePackRpcSerializer serializer) =>
        new(serializer, SendNoopAsync, exceptionTransformer: null);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    public enum StreamingContextAccessor
    {
        Stream,
        Pipe,
        AsyncEnumerable,
    }
}
