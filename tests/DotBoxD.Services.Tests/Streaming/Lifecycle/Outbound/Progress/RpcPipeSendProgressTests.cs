using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Progress;

public sealed class RpcPipeSendProgressTests
{
    [Theory]
    [InlineData("BeforeCredit", false)]
    [InlineData("BeforeCredit", true)]
    [InlineData("AfterFirstCredit", false)]
    [InlineData("AfterFirstCredit", true)]
    [InlineData("SyncFailure", false)]
    [InlineData("SyncFailure", true)]
    [InlineData("AsyncFailure", false)]
    [InlineData("AsyncFailure", true)]
    [InlineData("SyncFailureAfterFirst", false)]
    [InlineData("SyncFailureAfterFirst", true)]
    [InlineData("AsyncFailureAfterFirst", false)]
    [InlineData("AsyncFailureAfterFirst", true)]
    [InlineData("SendCanceled", false)]
    [InlineData("SendCanceled", true)]
    [InlineData("CancelAfterSuccess", false)]
    [InlineData("CancelAfterSuccess", true)]
    public async Task Failed_pump_preserves_only_unsent_segments(string mode, bool writerCompleted)
    {
        await using var fixture = await PipeSendProgressFixture.Create(mode, writerCompleted);

        await fixture.FailPump();
        await fixture.CompleteWriter();
        var remaining = await fixture.ReadRemaining();

        Assert.Equal(fixture.Payloads.Take(fixture.ExpectedSentBeforeFailure).SelectMany(bytes => bytes), fixture.Sent.SelectMany(bytes => bytes));
        Assert.Equal(fixture.Payloads.Skip(fixture.ExpectedSentBeforeFailure).SelectMany(bytes => bytes), remaining);
    }

    [Theory]
    [InlineData("BeforeCredit")]
    [InlineData("AfterFirstCredit")]
    [InlineData("SyncFailure")]
    [InlineData("AsyncFailureAfterFirst")]
    public async Task Failed_owned_pump_still_completes_reader(string mode)
    {
        await using var fixture = await PipeSendProgressFixture.Create(mode, writerCompleted: true, owned: true);

        await fixture.FailPump();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ReadRemaining());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Successful_pump_consumes_every_sent_segment(int segments, bool writerCompleted)
    {
        await using var fixture = await PipeSendProgressFixture.Create("Success", writerCompleted, segments: segments);
        var pump = fixture.Pump();
        if (!writerCompleted)
        {
            Assert.False(pump.IsCompleted);
            await fixture.CompleteWriter();
        }

        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(fixture.Payloads.SelectMany(bytes => bytes), fixture.Sent.SelectMany(bytes => bytes));
        Assert.Empty(await fixture.ReadRemaining());
    }

    [Theory]
    [InlineData("BeforeCredit")]
    [InlineData("AfterFirstCredit")]
    public async Task Borrowed_attachment_can_resume_without_losing_or_repeating_bytes(string mode)
    {
        await using var fixture = await PipeSendProgressFixture.Create(mode, writerCompleted: true);
        await fixture.FailPump();

        var resumed = await fixture.Resume();

        Assert.Equal(fixture.Payloads.SelectMany(bytes => bytes), fixture.Sent.SelectMany(bytes => bytes).Concat(resumed));
        Assert.Equal(fixture.Payloads.Skip(fixture.ExpectedSentBeforeFailure).SelectMany(bytes => bytes), resumed);
    }
}
