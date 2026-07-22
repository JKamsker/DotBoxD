using DotBoxD.Services.Buffers;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.Buffers;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PooledBufferWriterCacheCollection
{
    public const string Name = "PooledBufferWriter cache";
}

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class PooledBufferWriterCacheTests
{
    [Fact]
    public void Hot_cache_retains_only_small_buffers()
    {
        var writer = PooledBufferWriter.Rent(256);
        writer.Dispose();
        Assert.InRange(writer.RetainedBufferLength, 1, 4096);

        var largeWriter = PooledBufferWriter.Rent(8192);
        try
        {
            Assert.Same(writer, largeWriter);
        }
        finally
        {
            largeWriter.Dispose();
        }

        Assert.Equal(0, writer.RetainedBufferLength);
    }

    [Fact]
    public void Reused_buffer_still_enforces_the_new_written_budget()
    {
        var writer = PooledBufferWriter.Rent(256, 256);
        writer.Dispose();

        var reused = PooledBufferWriter.Rent(9, 9);
        try
        {
            Assert.Same(writer, reused);
            Assert.Throws<InvalidDataException>(() => reused.GetSpan(10));
        }
        finally
        {
            reused.Dispose();
        }
    }

    [Fact]
    public void Cross_thread_returns_never_lease_one_writer_twice()
    {
        const int writerCount = 1024;
        var firstLeases = RentDistinct(writerCount);
        Exception? returnFailure = null;
        var returnThread = new Thread(() =>
        {
            try
            {
                DisposeAll(firstLeases);
            }
            catch (Exception ex)
            {
                returnFailure = ex;
            }
        });

        returnThread.Start();
        returnThread.Join();
        Assert.Null(returnFailure);

        var secondLeases = RentDistinct(writerCount);
        Assert.True(
            new HashSet<PooledBufferWriter>(firstLeases).SetEquals(secondLeases),
            "The cross-thread cache did not preserve every returned writer.");
        DisposeAll(secondLeases);
    }

    private static PooledBufferWriter[] RentDistinct(int count)
    {
        var leases = new PooledBufferWriter[count];
        var distinct = new HashSet<PooledBufferWriter>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                var writer = PooledBufferWriter.Rent();
                leases[i] = writer;
                Assert.True(distinct.Add(writer), "A cached writer was leased more than once.");
            }

            return leases;
        }
        catch
        {
            DisposeAll(leases);
            throw;
        }
    }

    private static void DisposeAll(IEnumerable<PooledBufferWriter?> writers)
    {
        foreach (var writer in writers)
        {
            writer?.Dispose();
        }
    }
}
