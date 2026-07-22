using DotBoxD.Services.Buffers;
using DotBoxD.Services.Tests.Protocol.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class RpcFrameLifecycleRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void PayloadBackedConstructor_RejectsNullPayload()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new RpcFrame((Payload)null!));

        Assert.Equal("payload", ex.ParamName);
    }

    [Fact]
    public void PayloadBackedCopy_AfterOwnerDispose_FailsClosed()
    {
        var payload = Payload.Rent(3);
        new byte[] { 1, 2, 3 }.CopyTo(payload.Memory.Span);
        var owner = new RpcFrame(payload);
        var copy = owner;

        owner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => copy.Memory);
        Assert.Throws<ObjectDisposedException>(() => copy.DetachPayload());
    }

    [Fact]
    public void WriterBackedConstructor_RejectsNullWriter()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new RpcFrame((PooledBufferWriter)null!));

        Assert.Equal("writer", ex.ParamName);
    }

    [Fact]
    public void WriterBackedConstructor_RejectsConsumedWriter()
    {
        var writer = new PooledBufferWriter(1);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => new RpcFrame(writer));
    }

    [Fact]
    public void WriterBackedCopy_AfterOwnerDispose_FailsClosed()
    {
        var writer = new PooledBufferWriter(3);
        new byte[] { 1, 2, 3 }.CopyTo(writer.GetSpan(3));
        writer.Advance(3);
        var owner = new RpcFrame(writer);
        var copy = owner;

        owner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => copy.Memory);
        Assert.Throws<ObjectDisposedException>(() => copy.DetachPayload());
    }

    [Fact]
    public void WriterBackedCopy_AfterWriterReuse_CannotConsumeTheNewLease()
    {
        var writer = PooledBufferWriter.Rent(3);
        writer.Advance(1);
        var owner = new RpcFrame(writer);
        var staleRead = owner;
        var staleDetach = owner;
        var staleDispose = owner;
        owner.Dispose();
        var reused = PooledBufferWriter.Rent(3);
        try
        {
            Assert.Same(writer, reused);
            new byte[] { 4, 5, 6 }.CopyTo(reused.GetSpan(3));
            reused.Advance(3);

            Assert.Throws<ObjectDisposedException>(() => staleRead.Memory);
            Assert.Throws<ObjectDisposedException>(() => staleRead.Length);
            Assert.Throws<ObjectDisposedException>(() => staleDetach.DetachPayload());
            staleDispose.Dispose();

            Assert.Equal(3, reused.WrittenCount);
            Assert.Equal(new byte[] { 4, 5, 6 }, reused.WrittenMemory.ToArray());
        }
        finally
        {
            reused.Dispose();
        }
    }

    [Fact]
    public void WriterBackedCopy_AfterDetach_CannotConsumeTheReturnedWriter()
    {
        var writer = PooledBufferWriter.Rent(3);
        new byte[] { 1, 2, 3 }.CopyTo(writer.GetSpan(3));
        writer.Advance(3);
        var owner = new RpcFrame(writer);
        var staleCopy = owner;

        using var payload = owner.DetachPayload();
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Memory.ToArray());
        Assert.Throws<ObjectDisposedException>(() => staleCopy.Memory);
        Assert.Throws<ObjectDisposedException>(() => staleCopy.DetachPayload());
        staleCopy.Dispose();

        var reused = PooledBufferWriter.Rent(3);
        try
        {
            Assert.Same(writer, reused);
            _ = reused.WrittenMemory;
        }
        finally
        {
            reused.Dispose();
        }
    }

    [Fact]
    public async Task WriterBackedCopies_ConcurrentDetachAndDispose_CompleteOwnershipExactlyOnce()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var writer = new PooledBufferWriter(1);
            writer.GetSpan(1)[0] = 42;
            writer.Advance(1);
            var detachCopy = new RpcFrame(writer);
            var disposeCopy = detachCopy;
            using var start = new ManualResetEventSlim();
            Payload? payload = null;
            Exception? detachFailure = null;

            var detach = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    payload = detachCopy.DetachPayload();
                }
                catch (Exception ex)
                {
                    detachFailure = ex;
                }
            });
            var dispose = Task.Run(() =>
            {
                start.Wait();
                disposeCopy.Dispose();
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
                Assert.IsType<ObjectDisposedException>(detachFailure);
            }

            Assert.Throws<ObjectDisposedException>(() => writer.WrittenMemory);
            writer.Dispose();
        }
    }

    [Fact]
    public async Task StreamConnection_SendFrameValueAsync_RejectsNullFrame()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await connection.SendFrameValueAsync(null!));

        Assert.Equal("frame", ex.ParamName);
    }
}
