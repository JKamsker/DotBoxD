using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Generated;

public sealed class CompiledObserverContextLifetimeTests
{
    [Theory]
    [InlineData("ArtifactDelegate")]
    [InlineData("ArtifactCompiler")]
    [InlineData("ExecutableDelegate")]
    [InlineData("ExecutableCache")]
    [InlineData("Materialized")]
    public async Task Canceled_waiter_releases_ambient_state_while_shared_work_remains_pending(string kind)
    {
        var (plan, artifact) = await CompiledCacheCancellationFixture.CreateInputsAsync();
        using var fixture = new CompiledCacheCancellationFixture(kind, plan, artifact);
        using var cancellation = new CancellationTokenSource();
        // Shared materialization keeps its own active context; the canceled waiter has a separate one.
        var survivor = fixture.RequestAsync(CancellationToken.None);
        Task<CompiledArtifact> request = null!;
        var reference = AmbientContextLifetime.Capture(() => request = fixture.RequestAsync(cancellation.Token));
        try
        {
            AmbientContextLifetime.AssertRetained(reference);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(fixture.Work.IsCompleted);

            await AmbientContextLifetime.AssertCollectedAsync(reference);
            Assert.False(fixture.Work.IsCompleted);
            GC.KeepAlive(request);
        }
        finally
        {
            fixture.Complete();
        }
        Assert.Same(artifact, await survivor.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, fixture.Calls);
    }
}
