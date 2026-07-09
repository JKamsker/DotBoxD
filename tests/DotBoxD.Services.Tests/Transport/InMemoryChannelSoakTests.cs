using DotBoxD.Services.Testing;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class InMemoryChannelSoakTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task Repeated_churn_and_fault_delays_preserve_frames_without_leaks()
    {
        var iterations = int.TryParse(Environment.GetEnvironmentVariable("DOTBOXD_SOAK_ITERATIONS"), out var configured)
            ? configured
            : 2_000;

        for (var i = 0; i < iterations; i++)
        {
            var (sender, receiver) = InMemoryRpcChannel.CreatePair(capacity: 1);
            await using var delayed = new FaultInjectingRpcChannel(sender, static async (operation, count, ct) =>
            {
                if (operation == RpcChannelOperation.Send && count % 17 == 0)
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                }
            });
            await using var target = receiver;
            var expected = BitConverter.GetBytes(i);

            await delayed.SendAsync(expected);
            using var received = await target.ReceiveAsync();

            Assert.Equal(expected, received.Memory.ToArray());
        }
    }

    [Fact]
    public async Task Stalled_receive_observes_cancellation_and_pair_remains_disposable()
    {
        var (first, second) = InMemoryRpcChannel.CreatePair();
        await using var sender = first;
        await using var receiver = second;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => receiver.ReceiveAsync(cancellation.Token));
    }
}
