using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class RpcFrameLifecycleRegressionTests
{
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
    public async Task StreamConnection_SendFrameValueAsync_RejectsNullFrame()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await connection.SendFrameValueAsync(null!));

        Assert.Equal("frame", ex.ParamName);
    }
}
