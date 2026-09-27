using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

public sealed class RpcAttachmentPendingClaimTests
{
    public static IEnumerable<object[]> PendingCases()
    {
        foreach (var operation in new[] { "StreamRead", "PipeSend", "ItemsMove", "ItemsDispose" })
        {
            foreach (var batch in new[] { false, true })
            {
                foreach (var otherManager in new[] { false, true })
                {
                    yield return [operation, batch, otherManager];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(PendingCases))]
    public async Task Disposed_set_retains_attachment_claim_until_pump_and_cleanup_finish(
        string operation, bool batch, bool otherManager)
    {
        await using var fixture = new AttachmentClaimFixture(operation, batch);
        await fixture.StartAsync();
        await fixture.Original.DisposeAsync().AsTask().WaitAsync(AttachmentClaimFixture.Timeout);
        Assert.False(fixture.Pump.IsCompleted);
        var manager = otherManager ? NewManager() : fixture.Manager;

        Assert.Throws<ServiceProtocolException>(() => fixture.Register(manager));
        Assert.Equal(0, manager.OutboundSenderCount);

        await fixture.CompletePumpAsync();
        var replacement = fixture.Register(manager);
        Assert.Equal(1, manager.OutboundSenderCount);
        await fixture.Original.DisposeAsync();
        Assert.Throws<ServiceProtocolException>(() => fixture.Register(manager));
        await replacement.DisposeAsync();
        await fixture.Register(manager).DisposeAsync();
        Assert.Equal(0, manager.OutboundSenderCount);
    }

    [Theory]
    [InlineData("StreamRead", false)]
    [InlineData("StreamRead", true)]
    [InlineData("PipeSend", false)]
    [InlineData("PipeSend", true)]
    [InlineData("ItemsMove", false)]
    [InlineData("ItemsMove", true)]
    [InlineData("ItemsDispose", false)]
    [InlineData("ItemsDispose", true)]
    public async Task Completed_pump_keeps_claim_until_set_is_disposed(string operation, bool batch)
    {
        await using var fixture = new AttachmentClaimFixture(operation, batch);
        await fixture.StartAsync();
        await fixture.CompletePumpAsync();

        Assert.Throws<ServiceProtocolException>(() => fixture.Register(fixture.Manager));

        await fixture.Original.DisposeAsync();
        await fixture.Register(fixture.Manager).DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unstarted_set_releases_claim_without_starting_source(bool batch)
    {
        await using var fixture = new AttachmentClaimFixture("StreamRead", batch);

        await fixture.Original.DisposeAsync();
        fixture.Original.Start();
        await fixture.Register(fixture.Manager).DisposeAsync();

        Assert.False(fixture.Entered.Task.IsCompleted);
        Assert.Equal(0, fixture.Manager.OutboundSenderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_pump_completion_and_disposal_release_only_the_original_claim(bool batch)
    {
        for (var i = 0; i < 128; i++)
        {
            await using var fixture = new AttachmentClaimFixture("StreamRead", batch);
            await fixture.StartAsync();
            var disposal = Task.Run(async () => await fixture.Original.DisposeAsync());
            fixture.Release.TrySetResult();
            await Task.WhenAll(disposal, fixture.Pump).WaitAsync(AttachmentClaimFixture.Timeout);

            var replacement = fixture.Register(fixture.Manager);
            await fixture.Original.DisposeAsync();
            fixture.Original.Start();
            Assert.Throws<ServiceProtocolException>(() => fixture.Register(fixture.Manager));
            Assert.Equal(1, fixture.Manager.OutboundSenderCount);
            await replacement.DisposeAsync();
            await fixture.Register(fixture.Manager).DisposeAsync();
        }
    }

    private static RpcStreamManager NewManager() =>
        new(new MessagePackRpcSerializer(), static (_, _) => Task.CompletedTask, null);
}
