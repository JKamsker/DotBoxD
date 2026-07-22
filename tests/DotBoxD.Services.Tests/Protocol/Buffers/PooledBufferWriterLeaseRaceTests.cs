using DotBoxD.Services.Buffers;
using Xunit;
using BufferPayload = DotBoxD.Services.Buffers.Payload;

namespace DotBoxD.Services.Tests.Protocol.Buffers;

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class PooledBufferWriterLeaseRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Concurrent_detach_and_dispose_have_exactly_one_owner()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            using var writer = new PooledBufferWriter(1);
            writer.GetSpan(1)[0] = 42;
            writer.Advance(1);
            using var start = new ManualResetEventSlim();
            BufferPayload? payload = null;
            Exception? detachFailure = null;

            var detach = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    payload = writer.DetachPayload();
                }
                catch (Exception ex)
                {
                    detachFailure = ex;
                }
            });
            var dispose = Task.Run(() =>
            {
                start.Wait();
                writer.Dispose();
            });

            start.Set();
            await Task.WhenAll(detach, dispose).WaitAsync(Timeout);

            if (payload is not null)
            {
                Assert.Null(detachFailure);
                Assert.Equal(42, payload.Memory.Span[0]);
                payload.Dispose();
            }
            else
            {
                Assert.IsType<InvalidOperationException>(detachFailure);
            }
        }
    }
}
