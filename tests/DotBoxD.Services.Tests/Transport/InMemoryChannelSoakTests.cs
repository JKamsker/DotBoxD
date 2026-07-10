using System.Diagnostics;
using DotBoxD.Services.Testing;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class InMemoryChannelSoakTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task Repeated_churn_and_fault_delays_preserve_frames_without_leaks()
    {
        var configuration = ReadConfiguration();
        var elapsed = Stopwatch.StartNew();

        for (var i = 0; i < configuration.Iterations || elapsed.Elapsed < configuration.MinimumDuration; i++)
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
            Assert.Equal(0, InMemoryRpcChannel.GetOutstandingQueuedPayloadCount(sender));
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

    [Fact]
    public async Task Send_transform_can_model_truncated_partial_delivery()
    {
        var (first, second) = InMemoryRpcChannel.CreatePair();
        await using var sender = new FaultInjectingRpcChannel(
            first,
            static (_, _, _) => ValueTask.CompletedTask,
            static (data, _, _) => new ValueTask<ReadOnlyMemory<byte>>(data[..^1]));
        await using var receiver = second;

        await sender.SendAsync(new byte[] { 1, 2, 3, 4 });
        using var received = await receiver.ReceiveAsync();

        Assert.Equal(new byte[] { 1, 2, 3 }, received.Memory.ToArray());
    }

    [Fact]
    public async Task Stalled_fault_plan_observes_send_cancellation()
    {
        var (first, second) = InMemoryRpcChannel.CreatePair();
        await using var stalled = new FaultInjectingRpcChannel(first, static async (operation, _, ct) =>
        {
            if (operation == RpcChannelOperation.Send)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        });
        await using var receiver = second;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stalled.SendAsync(new byte[] { 1 }, cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_races_and_disconnects_drain_queued_payloads()
    {
        const int raceIterations = 500;
        for (var i = 0; i < raceIterations; i++)
        {
            var (first, second) = InMemoryRpcChannel.CreatePair(capacity: 1);
            var sender = first;
            var receiver = second;
            using var cancellation = new CancellationTokenSource();
            var receive = receiver.ReceiveAsync(cancellation.Token);
            var send = sender.SendAsync(BitConverter.GetBytes(i));
            await cancellation.CancelAsync();

            try
            {
                using var payload = await receive;
            }
            catch (OperationCanceledException)
            {
            }

            await send;
            await receiver.DisposeAsync();
            await sender.DisposeAsync();
            Assert.Equal(0, InMemoryRpcChannel.GetOutstandingQueuedPayloadCount(sender));
        }
    }

    private static SoakConfiguration ReadConfiguration()
    {
        var iterations = int.TryParse(Environment.GetEnvironmentVariable("DOTBOXD_SOAK_ITERATIONS"), out var configured)
            ? configured
            : 2_000;
        Assert.True(iterations > 0, "DOTBOXD_SOAK_ITERATIONS must be greater than zero.");

        var duration = TimeSpan.Zero;
        var durationText = Environment.GetEnvironmentVariable("DOTBOXD_SOAK_DURATION_MINUTES");
        if (!string.IsNullOrWhiteSpace(durationText))
        {
            Assert.True(
                double.TryParse(
                    durationText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var minutes) && minutes > 0,
                "DOTBOXD_SOAK_DURATION_MINUTES must be a positive number.");
            duration = TimeSpan.FromMinutes(minutes);
        }

        return new SoakConfiguration(iterations, duration);
    }

    private readonly record struct SoakConfiguration(int Iterations, TimeSpan MinimumDuration);
}
