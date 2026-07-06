using Xunit;
using BufferPayload = DotBoxD.Services.Buffers.Payload;

namespace DotBoxD.Services.Tests.Protocol;

/// <summary>
/// Regression tests for <see cref="BufferPayload"/> disposal: the shared <see cref="BufferPayload.Empty"/>
/// singleton must stay reusable after Dispose, and Dispose must be idempotent and thread-safe so a
/// rented buffer can never be returned to the pool more than once.
/// </summary>
public sealed class PayloadTests
{
    [Fact]
    public void Empty_Dispose_KeepsSingletonReusable()
    {
        // Empty wraps a zero-length array; Dispose must never null it out or later use of the shared
        // singleton would throw ObjectDisposedException.
        BufferPayload.Empty.Dispose();
        BufferPayload.Empty.Dispose();

        Assert.Equal(0, BufferPayload.Empty.Length);
        Assert.True(BufferPayload.Empty.Span.IsEmpty);
        Assert.True(BufferPayload.Empty.Memory.IsEmpty);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var payload = BufferPayload.Rent(64);
        payload.Dispose();

        // A second dispose must not throw and must not return the buffer to the pool a second time.
        payload.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = payload.Memory);
    }

    [Fact]
    public async Task Dispose_CalledConcurrently_DoesNotDoubleReturn()
    {
        // With the Interlocked exchange only one disposer observes the non-null array and returns it,
        // so racing disposers can never double-return the same rented buffer to ArrayPool.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var payload = BufferPayload.Rent(128);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var racers = new Task[8];
            for (var i = 0; i < racers.Length; i++)
            {
                racers[i] = Task.Run(async () =>
                {
                    await start.Task;
                    payload.Dispose();
                });
            }

            start.SetResult(true);
            await Task.WhenAll(racers);
        }
    }
}
