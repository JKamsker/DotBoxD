using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Remote;
using Xunit;
using Xunit.Abstractions;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteStream;

public sealed class RpcRemoteStreamAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "AllocationMeasurement")]
    public void Buffered_reads_remain_allocation_free(bool arrayRead)
    {
        var manager = new RpcStreamManager(new MessagePackRpcSerializer(), static (_, _) => Task.CompletedTask, exceptionTransformer: null);
        var handle = new RpcStreamHandle(1, RpcStreamKind.Binary);
        var receiver = manager.RegisterInboundResponse(handle, CancellationToken.None);
        var payload = new byte[2_048];
        Array.Fill(payload, (byte)0x5A);
        Assert.True(manager.TryAcceptItem(handle.StreamId, MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, payload)));
        using Stream stream = new RpcRemoteStream(receiver);
        var buffer = new byte[1];
        for (var index = 0; index < 100; index++)
        {
            Assert.Equal(1, Read(stream, buffer, arrayRead));
        }

        const int iterations = 1_000;
        var bytesRead = 0;
        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < iterations; index++)
        {
            bytesRead += Read(stream, buffer, arrayRead);
            checksum += buffer[0];
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Buffered read allocations (array overload: {arrayRead}): {allocated} bytes for {iterations} reads.");
        Assert.Equal(iterations, bytesRead);
        Assert.Equal(iterations * 0x5A, checksum);
        Assert.Equal(0, allocated);
    }

    private static int Read(Stream stream, byte[] buffer, bool arrayRead) => arrayRead
        ? stream.ReadAsync(buffer, 0, 1).GetAwaiter().GetResult()
        : stream.ReadAsync(buffer.AsMemory()).GetAwaiter().GetResult();
}
