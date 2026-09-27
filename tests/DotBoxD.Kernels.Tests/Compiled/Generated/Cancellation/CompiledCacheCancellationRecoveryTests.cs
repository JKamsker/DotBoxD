namespace DotBoxD.Kernels.Tests.Compiled.Generated;

public sealed class CompiledCacheCancellationRecoveryTests
{
    [Theory]
    [InlineData("ArtifactDelegate")]
    [InlineData("ArtifactCompiler")]
    [InlineData("ExecutableDelegate")]
    [InlineData("ExecutableCache")]
    [InlineData("Materialized")]
    public async Task Surviving_waiter_receives_original_failure_and_a_later_call_retries(string kind)
    {
        var (plan, artifact) = await CompiledCacheCancellationFixture.CreateInputsAsync();
        using var fixture = new CompiledCacheCancellationFixture(kind, plan, artifact);
        using var cancellation = new CancellationTokenSource();
        var canceled = fixture.RequestAsync(cancellation.Token);
        var survivor = fixture.RequestAsync(CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(fixture.Work.IsCompleted);
        Assert.Equal(1, fixture.Calls);

        var error = new InvalidOperationException("Shared compilation failed.");
        fixture.Fail(error);
        Assert.Same(error, await Record.ExceptionAsync(() => survivor.WaitAsync(TimeSpan.FromSeconds(5))));

        fixture.Reset();
        fixture.Complete();
        Assert.Same(artifact, await fixture.RequestAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, fixture.Calls);
    }

    [Theory]
    [InlineData("ArtifactDelegate")]
    [InlineData("ArtifactCompiler")]
    [InlineData("ExecutableDelegate")]
    [InlineData("ExecutableCache")]
    [InlineData("Materialized")]
    public async Task Successful_shared_work_remains_cached_after_all_waiters_cancel(string kind)
    {
        var (plan, artifact) = await CompiledCacheCancellationFixture.CreateInputsAsync();
        using var fixture = new CompiledCacheCancellationFixture(kind, plan, artifact);
        using var cancellation = new CancellationTokenSource();
        var canceled = fixture.RequestAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(fixture.Work.IsCompleted);

        fixture.Complete();
        Assert.Same(artifact, await fixture.RequestAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, fixture.Calls);
    }
}
