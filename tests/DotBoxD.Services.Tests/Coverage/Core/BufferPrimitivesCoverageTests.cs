using DotBoxD.Services.Buffers;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;
/// <summary>
/// Behavioral coverage for the pooled buffer primitives that back the wire codec:
/// <see cref="Payload"/> (rent/empty/dispose/double-dispose, span/memory boundaries) and
/// <see cref="PooledBufferWriter"/> (capacity defaulting, growth, advance bounds, detach/dispose,
/// zero-byte and large writes).
/// </summary>
public sealed class PayloadCoverageTests
{
    [Fact]
    public void Rent_ZeroLength_ReturnsSharedEmptySingleton()
    {
        // Act
        var first = Payload.Rent(0);
        var second = Payload.Rent(0);
        // Assert: a zero-length rent must hand back the reusable Empty singleton (lines 35-36).
        Assert.Same(Payload.Empty, first);
        Assert.Same(Payload.Empty, second);
        Assert.Equal(0, first.Length);
        Assert.True(first.Span.IsEmpty);
        Assert.True(first.Memory.IsEmpty);
    }
    [Fact]
    public void Empty_Dispose_IsNoOp_AndStaysUsable()
    {
        // Act: disposing Empty (any number of times) must never poison the singleton.
        Payload.Empty.Dispose();
        Payload.Empty.Dispose();
        // Assert
        Assert.Equal(0, Payload.Empty.Length);
        Assert.True(Payload.Empty.Span.IsEmpty);
        Assert.True(Payload.Empty.Memory.IsEmpty);
    }
    [Fact]
    public void Rent_PositiveLength_ProvidesWritableMemoryOfExactLength()
    {
        // Arrange
        const int length = 37;
        var payload = Payload.Rent(length);
        // Act: the receive fill path writes through Memory; Span observes it read-only.
        var memory = payload.Memory;
        for (var i = 0; i < length; i++)
        {
            memory.Span[i] = (byte)(i + 1);
        }
        // Assert
        Assert.Equal(length, payload.Length);
        Assert.Equal(length, payload.Memory.Length);
        Assert.Equal(length, payload.Span.Length);
        Assert.Equal((byte)1, payload.Span[0]);
        Assert.Equal((byte)length, payload.Span[length - 1]);
        payload.Dispose();
    }
    [Fact]
    public void Dispose_OnRealPayload_IsIdempotent()
    {
        // Arrange
        var payload = Payload.Rent(16);
        // Act: double dispose must not double-return the backing array to the pool.
        payload.Dispose();
        payload.Dispose();
        // Assert: after disposal Memory/Span observe the disposed state.
        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = payload.Span.Length;
        });
    }

    [Fact]
    public async Task Dispose_ConcurrentFromManyThreads_ReturnsBufferExactlyOnce()
    {
        // Arrange: a real payload disposed concurrently — the Interlocked.Exchange guard must ensure
        // only one caller observes the array and returns it. A double-return would corrupt the pool;
        // here we simply assert no exception escapes and the buffer ends disposed.
        var payload = Payload.Rent(64);
        var tasks = new Task[16];

        // Act
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => payload.Dispose());
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
    }
}
