using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

public sealed class RpcStreamingContextCancellationClaimRegressionTests
{
    public static TheoryData<string, RpcStreamKind> InboundAccessors { get; } = new()
    {
        { "stream", RpcStreamKind.Binary },
        { "pipe", RpcStreamKind.Binary },
        { "items", RpcStreamKind.Items },
    };

    [Theory]
    [MemberData(nameof(InboundAccessors))]
    public void InboundAccessorRejectsCanceledDispatchBeforeClaimingDeclaredStream(
        string accessor,
        RpcStreamKind kind)
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(24_001, kind);
        streams.RegisterInbound(new[] { handle }, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = new RpcStreamingContext(streams, serializer, cts.Token, new[] { handle });

        try
        {
            var error = Record.Exception(() => _ = GetInboundSurface(context, accessor, handle));

            Assert.IsType<OperationCanceledException>(error);
            var unclaimed = Assert.Throws<ServiceProtocolException>(
                context.EnsureAllDeclaredInboundStreamsClaimed);
            Assert.Contains("was not claimed", unclaimed.Message);
        }
        finally
        {
            streams.RemoveInbound(handle.StreamId);
        }
    }

    private static object GetInboundSurface(
        RpcStreamingContext context,
        string accessor,
        RpcStreamHandle handle) =>
        accessor switch
        {
            "stream" => context.GetStream(handle),
            "pipe" => context.GetPipe(handle),
            "items" => context.GetAsyncEnumerable<int>(handle),
            _ => throw new ArgumentOutOfRangeException(nameof(accessor), accessor, "Unknown accessor."),
        };

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
