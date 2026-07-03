using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class RpcFrameLifecycleRegressionTests
{
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
}
