using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core.RemoteEnumerable;

public sealed class RpcRemoteAsyncEnumerableLifetimeTests
{
    [Theory]
    [InlineData("BeforeRead", "Enumerable")]
    [InlineData("BeforeRead", "Enumerator")]
    [InlineData("AfterRead", "Enumerable")]
    [InlineData("AfterRead", "Enumerator")]
    [InlineData("Completed", "Enumerable")]
    [InlineData("Completed", "Enumerator")]
    [InlineData("Failure", "Enumerable")]
    [InlineData("Failure", "Enumerator")]
    [InlineData("AfterRead", "Both")]
    [InlineData("Completed", "Both")]
    [InlineData("Exhausted", "Enumerable")]
    [InlineData("Exhausted", "Enumerator")]
    public async Task Retained_completed_enumerations_release_their_owned_references(string stage, string retain)
    {
        var fixture = RemoteEnumerableLifetimeFixture.Create(stage, retain);

        await fixture.AssertCollected();

        if (fixture.Enumerator is not null)
        {
            await fixture.Enumerator.DisposeAsync();
        }

        if (fixture.Enumerable is not null)
        {
            Assert.Throws<InvalidOperationException>(() => fixture.Enumerable.GetAsyncEnumerator());
        }

        Assert.Equal(stage is "Completed" or "Exhausted" ? 0 : 1, fixture.Tracking.Cancels);
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData("BeforeRead")]
    [InlineData("Completed")]
    public async Task Discarded_completed_enumerations_release_their_owned_references(string stage)
        => await RemoteEnumerableLifetimeFixture.Create(stage, "Neither").AssertCollected();

    [Fact]
    public async Task Unstarted_enumerables_preserve_their_receiver_and_serializer()
    {
        var fixture = RemoteEnumerableLifetimeFixture.Create("Unstarted", "Enumerable");
        RemoteEnumerableLifetimeFixture.Collect();
        Assert.True(fixture.Sender.IsAlive);
        Assert.True(fixture.Receiver.IsAlive);
        Assert.True(fixture.Serializer.IsAlive);
        await using var enumerator = fixture.Enumerable!.GetAsyncEnumerator();
        Assert.Throws<InvalidOperationException>(() => fixture.Enumerable.GetAsyncEnumerator());
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Active_enumerators_preserve_the_current_value_and_owned_references()
    {
        var fixture = RemoteEnumerableLifetimeFixture.Create("Live", "Both");
        RemoteEnumerableLifetimeFixture.Collect();
        Assert.True(fixture.Sender.IsAlive);
        Assert.True(fixture.Serializer.IsAlive);
        Assert.True(fixture.TokenSource.IsAlive);
        Assert.Same(fixture.Tracking.LastValue!.Target, fixture.Enumerator!.Current);
        Assert.Equal(42, fixture.Enumerator.Current.Number);
        await fixture.Enumerator.DisposeAsync();
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Disposed_pending_enumerations_release_their_owned_references()
    {
        var fixture = RemoteEnumerableLifetimeFixture.Create("Pending", "Both");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.PendingRead!.WaitAsync(TimeSpan.FromSeconds(5)));
        fixture.PendingRead = null;

        await fixture.AssertCollected();

        Assert.Equal(1, fixture.Tracking.Cancels);
        GC.KeepAlive(fixture);
    }
}
