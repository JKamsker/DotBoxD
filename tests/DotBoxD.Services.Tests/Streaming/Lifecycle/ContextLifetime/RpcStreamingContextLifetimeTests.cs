using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.ContextLifetime;

public sealed class RpcStreamingContextLifetimeTests
{
    public static IEnumerable<object[]> CompletedCases()
    {
        foreach (var operation in new[] { "Complete", "CompleteInbound", "Abandon", "AbandonResponse", "DispatchFailure", "SerializationFailure", "CanceledDispatch", "DisposeFailure", "UnknownService" })
        {
            yield return [operation, true];
            yield return [operation, false];
        }
    }

    [Theory]
    [MemberData(nameof(CompletedCases))]
    public async Task Completed_dispatch_releases_unused_context_references(string operation, bool retain)
    {
        var fixture = ContextLifetimeFixture.Create(operation, retain);

        await fixture.AssertCollected();

        Assert.Equal(operation is "Complete" or "CompleteInbound" or "Abandon" or "UnknownService" ? 0 : 1, fixture.State.DisposeCalls);
        if (fixture.Context is not null)
        {
            using var rejected = new MemoryStream();
            Assert.Throws<InvalidOperationException>(() => fixture.Context.SetResponse(rejected));
            await fixture.Context.AbandonResponseAsync();
        }

        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Active_context_keeps_its_connection_and_request_state_alive()
    {
        var fixture = ContextLifetimeFixture.Create("Active", retain: true);
        ContextLifetimeFixture.Collect();
        Assert.True(fixture.Serializer.IsAlive);
        Assert.True(fixture.Sender!.IsAlive);
        Assert.True(fixture.TokenSource!.IsAlive);
        await fixture.Context!.AbandonResponseAsync();
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Completed_dispatch_keeps_untransferred_response_until_abandonment()
    {
        var fixture = ContextLifetimeFixture.Create("PendingResponse", retain: true);
        ContextLifetimeFixture.Collect();
        Assert.True(fixture.Serializer.IsAlive);
        Assert.True(fixture.Sender!.IsAlive);
        Assert.True(fixture.State.Source!.IsAlive);
        Assert.Equal(0, fixture.State.DisposeCalls);

        await fixture.Context!.AbandonResponseAsync();
        await fixture.AssertCollected();

        Assert.Equal(1, fixture.State.DisposeCalls);
        GC.KeepAlive(fixture);
    }
}
