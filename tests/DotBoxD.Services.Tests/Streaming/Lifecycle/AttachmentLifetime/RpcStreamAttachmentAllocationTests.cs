using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.AttachmentLifetime;

public sealed class RpcStreamAttachmentAllocationTests
{
    [Theory]
    [InlineData(false, false, 192)]
    [InlineData(false, true, 192)]
    [InlineData(true, false, 280)]
    [InlineData(true, true, 280)]
    public async Task Pending_pump_stays_within_source_ownership_budget(bool usePipe, bool owned, long budget)
    {
        // One additional reference keeps the source alive if disposal finishes during a read.
        // The pre-cleanup budgets were 184 B for streams and 320 B for pipes.
        // Bounded pipe chunking removes the enumerator from the async state, reducing 328 B to 280 B.
        var serializer = new MessagePackRpcSerializer();
        var manager = AttachmentLifetimeFixture.NewManager();
        var minimum = long.MaxValue;
        for (var i = 0; i < 250; i++)
        {
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pipe = new Pipe();
            using var stream = new PendingStream(gate.Task);
            var handle = new RpcStreamHandle(1, RpcStreamKind.Binary);
            var attachment = usePipe
                ? RpcStreamAttachment.FromPipe(handle, pipe, completeReader: owned)
                : RpcStreamAttachment.FromStream(handle, stream, leaveOpen: !owned);
            // Keep the shared pool's current-thread slot warm after GC or other tests trim it.
            ArrayPool<byte>.Shared.Return(ArrayPool<byte>.Shared.Rent(64 * 1024));

            var before = GC.GetAllocatedBytesForCurrentThread();
            var pump = attachment.PumpCoreAsync(manager, serializer, CancellationToken.None);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.False(pump.IsCompleted);
            gate.SetResult(0);
            pipe.Writer.Complete();
            await pump;
            pipe.Reader.Complete();
            if (i >= 50)
            {
                minimum = Math.Min(minimum, allocated);
            }
        }

        Assert.True(minimum <= budget, $"Pending pump allocated {minimum} B; budget is {budget} B.");
    }

    [Fact]
    public async Task Pending_owned_disposal_preserves_allocation_budget()
    {
        var minimum = long.MaxValue;
        for (var i = 0; i < 250; i++)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stream = new PendingDisposeStream(gate.Task);
            var attachment = RpcStreamAttachment.FromStream(new RpcStreamHandle(1, RpcStreamKind.Binary), stream, leaveOpen: false);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var disposal = attachment.DisposeSourceOnceAsync();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.False(disposal.IsCompleted);
            gate.SetResult();
            await disposal;
            if (i >= 50)
            {
                minimum = Math.Min(minimum, allocated);
            }

            GC.KeepAlive(attachment);
        }

        Assert.True(minimum <= 112, $"Pending source disposal allocated {minimum} B; budget is 112 B.");
    }

    private sealed class PendingStream(Task<int> read) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(read);
        public override ValueTask DisposeAsync() => default;
    }

    private sealed class PendingDisposeStream(Task disposal) : MemoryStream
    {
        public override ValueTask DisposeAsync() => new(disposal);
    }
}
