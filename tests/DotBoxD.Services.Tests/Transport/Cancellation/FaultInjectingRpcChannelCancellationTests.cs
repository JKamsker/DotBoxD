using DotBoxD.Services.Testing;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class FaultInjectingRpcChannelCancellationTests
{
    [Fact]
    public async Task SendAsync_does_not_transform_after_fault_hook_cancels_caller_token()
    {
        var (innerSender, receiver) = InMemoryRpcChannel.CreatePair();
        using var cancellation = new CancellationTokenSource();
        var transformCalls = 0;
        await using var sender = new FaultInjectingRpcChannel(
            innerSender,
            (operation, _, _) =>
            {
                if (operation == RpcChannelOperation.Send)
                {
                    cancellation.Cancel();
                }

                return ValueTask.CompletedTask;
            },
            (data, _, _) =>
            {
                transformCalls++;
                return new ValueTask<ReadOnlyMemory<byte>>(data);
            });
        await using var target = receiver;

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(new byte[] { 1 }, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, transformCalls);

        using var receiveCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => target.ReceiveAsync(receiveCancellation.Token));
    }
}
