using DotBoxD.Services.Testing;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class InMemoryRpcChannelEmptySendRegressionTests
{
    [Fact]
    public async Task SendAsync_RejectsEmptyPayloadReservedForChannelClosedTerminal()
    {
        var (sender, receiver) = InMemoryRpcChannel.CreatePair();
        await using var senderLifetime = sender;
        await using var receiverLifetime = receiver;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(ReadOnlyMemory<byte>.Empty));

        Assert.Equal("data", exception.ParamName);
        Assert.True(sender.IsConnected);
        Assert.True(receiver.IsConnected);
    }

    [Fact]
    public async Task SendAsync_AllowsNonEmptyRawPayload()
    {
        var (sender, receiver) = InMemoryRpcChannel.CreatePair();
        await using var senderLifetime = sender;
        await using var receiverLifetime = receiver;
        var expected = new byte[] { 1 };

        await sender.SendAsync(expected);
        using var received = await receiver.ReceiveAsync();

        Assert.Equal(expected, received.Memory.ToArray());
    }
}
