using System.Collections.Concurrent;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

public sealed class TcpFrameReceiveOperationCacheTests
{
    [Fact]
    public void CreationAndReturn_AreBoundedToTwoSources()
    {
        var cache = new TcpFrameReceiveOperationCache();
        var first = Assert.IsType<TcpFrameReceiveOperation>(cache.TryCreate());
        var second = Assert.IsType<TcpFrameReceiveOperation>(cache.TryCreate());

        Assert.Null(cache.TryCreate());
        Assert.Equal(2, cache.CreatedCount);
        cache.Return(first);
        cache.Return(second);
        Assert.Equal(2, cache.AvailableCount);

        Assert.Contains(cache.TryRent(), new[] { first, second });
        Assert.NotNull(cache.TryRent());
        Assert.Null(cache.TryRent());
    }

    [Fact]
    public void Dispose_DropsIdleAndLateReturningSources()
    {
        var cache = new TcpFrameReceiveOperationCache();
        var first = Assert.IsType<TcpFrameReceiveOperation>(cache.TryCreate());
        var second = Assert.IsType<TcpFrameReceiveOperation>(cache.TryCreate());
        cache.Return(first);
        cache.Return(second);
        var leased = Assert.IsType<TcpFrameReceiveOperation>(cache.TryRent());

        cache.Dispose();

        cache.Return(leased);
        Assert.Equal(0, cache.AvailableCount);
        Assert.Null(cache.TryRent());
        Assert.Null(cache.TryCreate());
    }

    [Fact]
    public async Task ConcurrentRentAndReturn_NeverDoubleLeasesOrCreatesThirdSource()
    {
        var cache = new TcpFrameReceiveOperationCache();
        var first = Assert.IsType<TcpFrameReceiveOperation>(cache.TryCreate());
        var second = Assert.IsType<TcpFrameReceiveOperation>(cache.TryCreate());
        cache.Return(first);
        cache.Return(second);
        var leased = new ConcurrentDictionary<TcpFrameReceiveOperation, byte>();
        using var start = new Barrier(participantCount: 9);

        var workers = Enumerable.Range(0, 8).Select(workerIndex => Task.Run(() =>
        {
            _ = workerIndex;
            start.SignalAndWait();
            for (var iteration = 0; iteration < 2_000; iteration++)
            {
                var operation = cache.TryRent();
                if (operation is null)
                {
                    Thread.Yield();
                    continue;
                }

                Assert.True(leased.TryAdd(operation, 0), "A TCP receive source was leased twice.");
                Thread.Yield();
                Assert.True(leased.TryRemove(operation, out var removed));
                Assert.Equal(0, removed);
                cache.Return(operation);
            }
        })).ToArray();

        start.SignalAndWait();
        await Task.WhenAll(workers);

        Assert.Empty(leased);
        Assert.Equal(2, cache.CreatedCount);
        Assert.Equal(2, cache.AvailableCount);
        Assert.Null(cache.TryCreate());
    }
}
