using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Chunks;

public sealed class RpcStreamChunkLifetimeTests
{
    [Theory]
    [InlineData(1, true, false)]
    [InlineData(4, true, false)]
    [InlineData(1, false, false)]
    [InlineData(4, false, false)]
    [InlineData(1, true, true)]
    [InlineData(4, true, true)]
    [InlineData(1, false, true)]
    [InlineData(4, false, true)]
    public async Task Disposed_chunks_release_owners_even_when_the_read_result_is_retained(int count, bool credit, bool retainRead)
    {
        using var fixture = ChunkLifetimeFixture.Create(count, dispose: true, credit, retainRead);
        await fixture.AssertCollectedAsync();
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Live_chunks_keep_their_owners(int count)
    {
        using var fixture = ChunkLifetimeFixture.Create(count, dispose: false, credit: false);
        ChunkLifetimeFixture.Collect();
        Assert.All(fixture.References, reference => Assert.True(reference.IsAlive));
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Discarded_chunks_do_not_have_an_independent_owner_root(int count)
    {
        using var fixture = ChunkLifetimeFixture.Create(count, dispose: true, credit: false, retain: false);
        await fixture.AssertCollectedAsync();
    }

    [Fact]
    public async Task In_flight_credit_keeps_its_owner_until_sending_finishes()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = ChunkLifetimeFixture.CreateWithPendingCredit(release.Task);
        try
        {
            ChunkLifetimeFixture.Collect();
            Assert.All(fixture.References, reference => Assert.True(reference.IsAlive));
        }
        finally
        {
            release.TrySetResult(true);
        }
        await fixture.AssertCollectedAsync();
        GC.KeepAlive(fixture);
    }
}
