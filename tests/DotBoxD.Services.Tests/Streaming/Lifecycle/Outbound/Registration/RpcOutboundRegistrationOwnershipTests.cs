using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Registration;

public sealed class RpcOutboundRegistrationOwnershipTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task OldCleanup_PreservesReplacementSenderAndItsCredits(int mode, bool batch)
    {
        await using var fixture = new OutboundRegistrationFixture(batch);
        await fixture.PrepareAsync(mode);
        var id = fixture.Handle.StreamId;
        fixture.RegisterReplacement(id);
        fixture.AddCredit(id);

        await fixture.FinishOriginalAsync();

        Assert.Equal(1, fixture.Manager.OutboundSenderCount);
        await fixture.AssertReplacementSendsAsync(id);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task OldCleanup_PreservesReplacementReservationAndBufferedCredits(int mode, bool batch)
    {
        await using var fixture = new OutboundRegistrationFixture(batch);
        await fixture.PrepareAsync(mode);
        var id = fixture.Handle.StreamId;
        fixture.Manager.ReserveOutbound(id);
        fixture.AddCredit(id);

        await fixture.FinishOriginalAsync();

        Assert.Equal(1, fixture.Manager.PendingCreditCount);
        fixture.RegisterReplacement(id);
        Assert.Equal(0, fixture.Manager.PendingCreditCount);
        await fixture.AssertReplacementSendsAsync(id);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task OldCleanup_PreservesCancellationOfReplacementReservation(int mode, bool batch)
    {
        await using var fixture = new OutboundRegistrationFixture(batch);
        await fixture.PrepareAsync(mode);
        var id = fixture.Handle.StreamId;
        fixture.Manager.ReserveOutbound(id);
        fixture.Manager.CancelOutbound(id);

        await fixture.FinishOriginalAsync();

        using var source = new MemoryStream();
        var attachment = RpcStreamAttachment.FromStream(new(id, RpcStreamKind.Binary), source);
        Assert.Throws<OperationCanceledException>(() => fixture.Manager.RegisterOutbound(attachment, CancellationToken.None));
        Assert.Equal(0, fixture.Manager.OutboundSenderCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task OldCleanup_PreservesIndependentSender(int mode, bool batch)
    {
        await using var fixture = new OutboundRegistrationFixture(batch);
        await fixture.PrepareAsync(mode);
        var id = fixture.Manager.ReserveOutbound(RpcStreamKind.Binary).StreamId;
        fixture.RegisterReplacement(id);
        fixture.AddCredit(id);

        await fixture.FinishOriginalAsync();

        Assert.Equal(1, fixture.Manager.OutboundSenderCount);
        await fixture.AssertReplacementSendsAsync(id);
    }
}
