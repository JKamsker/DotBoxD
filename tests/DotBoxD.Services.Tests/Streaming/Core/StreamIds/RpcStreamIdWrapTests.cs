using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.StreamIds;

public sealed class RpcStreamIdWrapTests
{
    public static IEnumerable<object[]> Boundaries()
    {
        foreach (var kind in new[] { RpcStreamKind.Binary, RpcStreamKind.Items })
        {
            foreach (var counter in new[] { 0, 1, int.MaxValue - 1, int.MaxValue, int.MinValue, -1, -2 })
            {
                yield return [kind, counter];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task Reservation_advances_promptly_across_counter_boundaries(RpcStreamKind kind, int counter)
    {
        var manager = NewManager();
        manager.OutboundStreamIdCounterForTest = counter;
        try
        {
            var handles = await ReservePromptly(manager, () =>
                [manager.ReserveOutbound(kind), manager.ReserveOutbound(kind), manager.ReserveOutbound(kind)]);
            var expected = counter < 0 || counter == int.MaxValue ? 1 : counter + 1;
            foreach (var handle in handles)
            {
                Assert.Equal(expected, handle.StreamId);
                Assert.Equal(kind, handle.Kind);
                expected = expected == int.MaxValue ? 1 : expected + 1;
            }
        }
        finally
        {
            manager.Stop();
        }
    }

    [Theory]
    [InlineData(RpcStreamKind.Binary, false)]
    [InlineData(RpcStreamKind.Binary, true)]
    [InlineData(RpcStreamKind.Items, false)]
    [InlineData(RpcStreamKind.Items, true)]
    public async Task Wrap_preserves_reserved_and_registered_ids(RpcStreamKind kind, bool active)
    {
        var manager = NewManager();
        manager.ReserveOutbound(1);
        manager.ReserveOutbound(3);
        using var source = new MemoryStream();
        RpcOutboundStreamSet? outbound = null;
        try
        {
            if (active)
            {
                outbound = manager.RegisterOutbound(RpcStreamAttachment.FromStream(new(2, RpcStreamKind.Binary), source),
                    CancellationToken.None);
            }
            else
            {
                manager.ReserveOutbound(2);
            }
            manager.OutboundStreamIdCounterForTest = int.MaxValue;

            var handles = await ReservePromptly(manager, () =>
                [manager.ReserveOutbound(kind), manager.ReserveOutbound(kind), manager.ReserveOutbound(kind)]);

            Assert.Equal(new[] { 4, 5, 6 }, handles.Select(handle => handle.StreamId));
            Assert.Equal(active ? 1 : 0, manager.OutboundSenderCount);
        }
        finally
        {
            if (outbound is not null)
            {
                await outbound.DisposeAsync();
            }
            manager.Stop();
        }
    }

    [Theory]
    [InlineData(RpcStreamKind.Binary)]
    [InlineData(RpcStreamKind.Items)]
    public async Task Concurrent_reservations_remain_unique_across_wrap(RpcStreamKind kind)
    {
        var manager = NewManager();
        manager.OutboundStreamIdCounterForTest = int.MaxValue - 4;
        try
        {
            var handles = await ReservePromptly(manager, () =>
            {
                var results = new RpcStreamHandle[256];
                Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                    index => results[index] = manager.ReserveOutbound(kind));
                return results;
            });

            Assert.Equal(256, handles.Select(handle => handle.StreamId).Distinct().Count());
            Assert.All(handles, handle =>
            {
                Assert.True(handle.StreamId > 0);
                Assert.Equal(kind, handle.Kind);
            });
        }
        finally
        {
            manager.Stop();
        }
    }

    internal static RpcStreamManager NewManager() =>
        new(new MessagePackRpcSerializer(), static (_, _) => Task.CompletedTask, null);

    private static async Task<RpcStreamHandle[]> ReservePromptly(RpcStreamManager manager, Func<RpcStreamHandle[]> reserve)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(() =>
        {
            entered.TrySetResult();
            return reserve();
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            // Bound a regression's CPU work instead of leaving a loop traversing 2^31 IDs.
            if (!task.IsCompleted)
            {
                manager.OutboundStreamIdCounterForTest = 0;
                await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}
