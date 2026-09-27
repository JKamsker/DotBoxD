using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

public sealed class RpcAttachmentSetupOwnershipTests
{
    [Theory]
    [InlineData("Duplicate", false)]
    [InlineData("Duplicate", true)]
    [InlineData("InvalidTarget", false)]
    [InlineData("InvalidTarget", true)]
    [InlineData("PreCanceled", false)]
    [InlineData("PreCanceled", true)]
    [InlineData("PendingLimit", false)]
    [InlineData("PendingLimit", true)]
    public async Task Failed_call_preserves_source_owned_by_an_active_call(string failure, bool batch)
    {
        await using var fixture = new OwnedAttachmentSetupFixture(batch, failure == "PendingLimit");
        await fixture.StartOwnerAsync();
        using var cancellation = new CancellationTokenSource();
        if (failure == "PreCanceled")
        {
            cancellation.Cancel();
        }

        var error = await Record.ExceptionAsync(() => fixture.InvokeAsync(fixture.Attachment, cancellation.Token,
            failure == "InvalidTarget" ? "" : "Receiver").WaitAsync(OwnedAttachmentSetupFixture.Timeout));

        Assert.IsType(ExpectedFailure(failure), error);
        Assert.Equal(0, fixture.Source.Disposals);
        Assert.False(fixture.Source.Finished.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_batch_cleans_unregistered_source_and_preserves_active_source(bool activeFirst)
    {
        await using var fixture = new OwnedAttachmentSetupFixture(batch: true);
        await fixture.StartOwnerAsync();
        await using var unregisteredSource = new OwnedAttachmentSetupFixture.TrackingStream();
        var unregistered = RpcStreamAttachment.FromStream(fixture.Peer.ReserveStream(RpcStreamKind.Binary),
            unregisteredSource, leaveOpen: false);
        RpcStreamAttachment[] attachments = activeFirst
            ? [fixture.Attachment, unregistered]
            : [unregistered, fixture.Attachment];

        await Assert.ThrowsAsync<ServiceProtocolException>(() => fixture.Peer.InvokeAsync<int, int>(
            "Receiver", "Read", 0, attachments).WaitAsync(OwnedAttachmentSetupFixture.Timeout));

        Assert.Equal(1, unregisteredSource.Disposals);
        Assert.Equal(0, fixture.Source.Disposals);
        Assert.False(unregisteredSource.Started.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Validation_failure_cleans_only_unregistered_owned_sources(bool batch, bool owned)
    {
        await using var fixture = new OwnedAttachmentSetupFixture(batch);
        await using var source = new OwnedAttachmentSetupFixture.TrackingStream();
        var attachment = RpcStreamAttachment.FromStream(fixture.Peer.ReserveStream(RpcStreamKind.Binary),
            source, leaveOpen: !owned);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.InvokeAsync(attachment, service: "")
            .WaitAsync(OwnedAttachmentSetupFixture.Timeout));

        Assert.Equal(owned ? 1 : 0, source.Disposals);
        Assert.False(source.Started.Task.IsCompleted);
    }

    private static Type ExpectedFailure(string failure) => failure switch
    {
        "Duplicate" => typeof(ServiceProtocolException),
        "InvalidTarget" => typeof(ArgumentException),
        "PreCanceled" => typeof(OperationCanceledException),
        _ => typeof(ServiceException),
    };
}
