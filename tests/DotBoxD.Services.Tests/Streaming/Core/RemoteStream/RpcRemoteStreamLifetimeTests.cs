using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteStream;

public sealed class RpcRemoteStreamLifetimeTests
{
    [Theory]
    [InlineData("Empty", false)]
    [InlineData("Empty", true)]
    [InlineData("Buffered", false)]
    [InlineData("Buffered", true)]
    [InlineData("Partial", false)]
    [InlineData("Partial", true)]
    [InlineData("Consumed", false)]
    [InlineData("Consumed", true)]
    public async Task Retained_disposed_streams_release_receivers_and_sender_callbacks(string stage, bool asynchronousDispose)
    {
        var fixture = RemoteStreamLifetimeFixture.Create(stage, asynchronousDispose);

        await fixture.AssertCollected();

        RemoteStreamLifetimeFixture.Dispose(fixture.Stream!, asynchronousDispose);
        Assert.Equal(1, fixture.State.Cancels);
        Assert.Equal(1, fixture.State.Credits);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Stream!.ReadAsync(new byte[1]).AsTask());
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Discarded_disposed_streams_release_receivers_and_sender_callbacks()
    {
        var fixture = RemoteStreamLifetimeFixture.Create("Partial", asynchronousDispose: false, retainStream: false);
        await fixture.AssertCollected();
    }

    [Fact]
    public async Task Live_streams_keep_their_receiver_and_sender_available()
    {
        var fixture = RemoteStreamLifetimeFixture.Create("Live", asynchronousDispose: false);
        RemoteStreamLifetimeFixture.Collect();
        Assert.True(fixture.Sender.IsAlive);
        Assert.True(fixture.Receiver.IsAlive);
        var buffer = new byte[2];
        Assert.Equal(2, await fixture.Stream!.ReadAsync(buffer));
        Assert.Equal(new byte[] { 1, 2 }, buffer);
        fixture.Stream.Dispose();
        Assert.Equal(1, fixture.State.Cancels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_cancels_pending_reads_and_releases_their_receiver(bool asynchronousDispose)
    {
        var fixture = RemoteStreamLifetimeFixture.Create("Pending", asynchronousDispose);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.PendingRead!.WaitAsync(TimeSpan.FromSeconds(5)));
        fixture.PendingRead = null;

        await fixture.AssertCollected();

        Assert.Equal(1, fixture.State.Cancels);
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Pending_cancel_notification_keeps_sender_alive_only_until_it_finishes()
    {
        var fixture = RemoteStreamLifetimeFixture.Create("Partial", asynchronousDispose: false, pauseCancel: true);
        RemoteStreamLifetimeFixture.Collect();
        Assert.True(fixture.Sender.IsAlive);
        Assert.Equal(0, fixture.State.CompletedCancels);

        fixture.State.AllowCancel.SetResult(true);
        await fixture.State.CancelFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.AssertCollected();

        Assert.Equal(1, fixture.State.CompletedCancels);
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Concurrent_disposal_sends_one_cancel_and_no_extra_credit()
    {
        var fixture = RemoteStreamLifetimeFixture.Create("Live", asynchronousDispose: false);
        Assert.Equal(1, await fixture.Stream!.ReadAsync(new byte[1]));

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(fixture.Stream.Dispose)));

        Assert.Equal(1, fixture.State.Cancels);
        Assert.Equal(1, fixture.State.Credits);
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incoming_data_racing_disposal_does_not_restore_retained_receivers(bool arrayRead)
    {
        var fixtures = new List<RemoteStreamLifetimeFixture>();
        for (var iteration = 0; iteration < 128; iteration++)
        {
            fixtures.Add(await RemoteStreamLifetimeFixture.RaceReadAndDispose(arrayRead));
        }

        foreach (var fixture in fixtures)
        {
            await fixture.AssertCollected();
            Assert.Equal(1, fixture.State.Cancels);
        }

        GC.KeepAlive(fixtures);
    }
}
