using DotBoxD.Services.Streaming.Core;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Chunking;

public sealed class RpcPipeChunkProgressTests
{
    [Theory]
    [InlineData("Credit", false, false)]
    [InlineData("Credit", false, true)]
    [InlineData("Credit", true, false)]
    [InlineData("Credit", true, true)]
    [InlineData("Sync", false, false)]
    [InlineData("Sync", false, true)]
    [InlineData("Sync", true, false)]
    [InlineData("Sync", true, true)]
    [InlineData("Async", false, false)]
    [InlineData("Async", false, true)]
    [InlineData("Async", true, false)]
    [InlineData("Async", true, true)]
    public async Task Partial_segment_failure_preserves_only_unsent_bytes(string failure, bool firstChunkSent, bool owned)
        => await AssertFailure(failure, firstChunkSent, owned, [2 * PipeChunkingFixture.ChunkSize + 7]);

    [Theory]
    [InlineData("Credit", false, false)]
    [InlineData("Credit", true, false)]
    [InlineData("Sync", false, false)]
    [InlineData("Sync", true, false)]
    [InlineData("Async", false, false)]
    [InlineData("Async", true, false)]
    [InlineData("Credit", false, true)]
    [InlineData("Credit", true, true)]
    [InlineData("Sync", false, true)]
    [InlineData("Sync", true, true)]
    [InlineData("Async", false, true)]
    [InlineData("Async", true, true)]
    public async Task Coalesced_frame_failure_preserves_only_unsent_bytes(string failure, bool firstChunkSent, bool owned)
        => await AssertFailure(failure, firstChunkSent, owned, Enumerable.Repeat(4096, 33).ToArray());

    private static async Task AssertFailure(string failure, bool firstChunkSent, bool owned, int[] lengths)
    {
        var credits = failure == "Credit" ? firstChunkSent ? 1 : 0 : RpcStreamManager.WindowSize;
        await using var fixture = await PipeChunkingFixture.Create(lengths, owned, credits: credits);
        fixture.ReplenishCredit = false;
        fixture.FailAt = failure == "Credit" ? 0 : firstChunkSent ? 2 : 1;
        fixture.DeferFailure = failure == "Async";

        var pump = fixture.Pump();
        var expectedBytes = firstChunkSent ? PipeChunkingFixture.ChunkSize : 0;
        try
        {
            Assert.Equal(expectedBytes, fixture.BytesSent);
        }
        finally
        {
            if (failure == "Credit")
            {
                fixture.Cancellation.Cancel();
            }
            else if (failure == "Async")
            {
                fixture.SendFailure.TrySetException(fixture.ExpectedFailure);
                _ = fixture.SendFailure.Task.Exception;
            }
        }

        if (failure == "Credit")
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(fixture.Cancellation.Token, error.CancellationToken);
        }
        else
        {
            Assert.Same(fixture.ExpectedFailure, await Assert.ThrowsAsync<IOException>(() => pump.WaitAsync(TimeSpan.FromSeconds(5))));
        }

        await fixture.AssertRemaining(expectedBytes);
    }
}
