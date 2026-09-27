using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.AttachmentLifetime;

public sealed class RpcStreamAttachmentLifetimeTests
{
    [Theory]
    [InlineData("StreamSuccess", true)]
    [InlineData("StreamSuccess", false)]
    [InlineData("PipeSuccess", true)]
    [InlineData("PipeSuccess", false)]
    [InlineData("StreamFailedRead", true)]
    [InlineData("StreamFailedRead", false)]
    [InlineData("PipeFailedRead", true)]
    [InlineData("PipeFailedRead", false)]
    [InlineData("StreamFailedDispose", true)]
    [InlineData("StreamFailedDispose", false)]
    [InlineData("StreamUnstarted", true)]
    [InlineData("StreamUnstarted", false)]
    [InlineData("PipeUnstarted", true)]
    [InlineData("PipeUnstarted", false)]
    [InlineData("StreamDirect", true)]
    [InlineData("StreamDirect", false)]
    [InlineData("PipeDirect", true)]
    [InlineData("PipeDirect", false)]
    public async Task Completed_owned_attachment_releases_source(string operation, bool retain)
    {
        var fixture = await AttachmentLifetimeFixture.Exercise(operation, retain);

        await fixture.AssertCollected();

        if (fixture.Attachment is not null)
        {
            Assert.Equal(1, fixture.Attachment.Handle.StreamId);
            Assert.Throws<ServiceProtocolException>(() => AttachmentLifetimeFixture.NewManager()
                .RegisterOutbound(fixture.Attachment, CancellationToken.None));
            await fixture.Attachment.DisposeSourceOnceAsync();
            if (operation.StartsWith("Stream", StringComparison.Ordinal))
            {
                Assert.Equal(1, fixture.State.DisposeCalls);
            }
        }

        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData("StreamSuccess")]
    [InlineData("PipeSuccess")]
    public async Task Borrowed_attachment_keeps_source_and_can_be_reused(string operation)
    {
        var fixture = AttachmentLifetimeFixture.Create(operation, owned: false);
        for (var i = 0; i < 2; i++)
        {
            await using var outbound = AttachmentLifetimeFixture.NewManager()
                .RegisterOutbound(fixture.Attachment!, CancellationToken.None);
            outbound.Start();
            await outbound.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        AttachmentLifetimeFixture.Collect();
        Assert.True(fixture.Source.IsAlive);
        Assert.Equal(0, fixture.State.DisposeCalls);
        if (operation.StartsWith("Stream", StringComparison.Ordinal))
        {
            Assert.Equal(2, fixture.State.ReadCalls);
        }

        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_remains_owned_until_async_disposal_finishes(bool fail)
    {
        var fixture = AttachmentLifetimeFixture.Create("StreamPendingDispose");
        var dispose = fixture.Attachment!.DisposeSourceOnceAsync();
        Assert.False(dispose.IsCompleted);
        AttachmentLifetimeFixture.Collect();
        Assert.True(fixture.Source.IsAlive);
        if (fail)
        {
            var expected = new IOException("Dispose failed");
            fixture.State.DisposeCompletion.SetException(expected);
            Assert.Same(expected, await Assert.ThrowsAsync<IOException>(() => dispose.AsTask()));
        }
        else
        {
            fixture.State.DisposeCompletion.SetResult();
            await dispose;
        }

        await fixture.AssertCollected();
        Assert.Equal(1, fixture.State.DisposeCalls);
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Disposal_during_pending_read_preserves_pump_ownership_until_completion()
    {
        var fixture = AttachmentLifetimeFixture.Create("StreamPendingRead");
        var pump = fixture.Attachment!.PumpCoreAsync(AttachmentLifetimeFixture.NewManager(),
            new MessagePackRpcSerializer(), CancellationToken.None);
        Assert.False(pump.IsCompleted);
        Assert.Equal(1, fixture.State.ReadCalls);

        await fixture.Attachment.DisposeSourceOnceAsync();
        AttachmentLifetimeFixture.Collect();
        Assert.True(fixture.Source.IsAlive);
        fixture.State.ReadCompletion.SetResult(0);
        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        await fixture.AssertCollected();
        Assert.Equal(1, fixture.State.DisposeCalls);
        GC.KeepAlive(fixture);
        GC.KeepAlive(pump);
    }
}
