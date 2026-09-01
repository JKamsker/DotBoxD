using DotBoxD.Services.Testing;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class InMemoryRpcChannelPreCanceledEmptySendTests
{
    [Fact]
    public async Task SendAsync_PreCanceledTokenWinsOverReservedEmptyPayloadValidation()
    {
        var (sender, receiver) = InMemoryRpcChannel.CreatePair();
        await using var senderLifetime = sender;
        await using var receiverLifetime = receiver;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(ReadOnlyMemory<byte>.Empty, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(sender.IsConnected);
        Assert.True(receiver.IsConnected);
        Assert.Equal(0, InMemoryRpcChannel.GetOutstandingQueuedPayloadCount(sender));
    }
}
