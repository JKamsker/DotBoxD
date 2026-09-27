namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheLifetime;

public sealed class CompiledProviderLifetimeTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public async Task Disposed_host_releases_compiler_and_cached_artifacts(bool custom, int count)
    {
        var probe = CompiledProviderLifetimeFixture.CreateDisposed(custom, count);
        using var host = probe.Host;
        await CompiledProviderLifetimeFixture.AssertCollectedAsync(probe.References);
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Live_host_keeps_its_compiler_and_artifacts_available(bool custom)
    {
        var probe = CompiledProviderLifetimeFixture.CreateLive(custom);
        using var host = probe.Host;
        CompiledProviderLifetimeFixture.Collect();
        Assert.All(probe.References, reference => Assert.True(reference.IsAlive));
        CompiledProviderLifetimeFixture.Execute(host, probe.Plan);
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disposing_one_host_preserves_the_shared_callers_compiler_and_cache(bool custom)
    {
        var compiler = CompiledProviderLifetimeFixture.Compiler(custom);
        using var first = CompiledProviderLifetimeFixture.Host(compiler);
        using var second = CompiledProviderLifetimeFixture.Host(compiler);
        var firstPlan = CompiledProviderLifetimeFixture.Prepare(first);
        var secondPlan = CompiledProviderLifetimeFixture.Prepare(second);
        var artifact = CompiledProviderLifetimeFixture.Compile(compiler, firstPlan);
        CompiledProviderLifetimeFixture.Execute(first, firstPlan);
        first.Dispose();

        CompiledProviderLifetimeFixture.AssertCallerCompilerOwned(compiler);
        Assert.Same(artifact, CompiledProviderLifetimeFixture.Compile(compiler, secondPlan));
        CompiledProviderLifetimeFixture.Execute(second, secondPlan);
    }
}
