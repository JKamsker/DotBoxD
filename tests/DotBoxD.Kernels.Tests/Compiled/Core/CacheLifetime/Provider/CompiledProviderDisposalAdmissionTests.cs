using DotBoxD.Hosting.Execution.Compiled;

namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheLifetime;

public sealed class CompiledProviderDisposalAdmissionTests
{
    [Fact]
    public async Task Unavailable_provider_stays_unavailable_and_rejects_requests_after_disposal()
    {
        using var host = SandboxHost.Create();
        var plan = CompiledProviderLifetimeFixture.Prepare(host);
        using var provider = new CompiledExecutionProvider(null);
        Assert.False(provider.IsAvailable);
        Assert.False(provider.CanPublishCompletedExecutable);
        provider.Dispose();

        Assert.False(provider.IsAvailable);
        Assert.False(provider.TryGetCompletedExecutable(plan, "main", out _));
        Assert.False(provider.TryGetCachedCompletedExecutable(plan, "main", out _));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await provider.GetAsync(plan, "main", CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_provider_does_not_start_custom_compilation(bool publish)
    {
        using var host = SandboxHost.Create();
        var plan = CompiledProviderLifetimeFixture.Prepare(host);
        var compiler = new CompiledProviderLifetimeFixture.PendingCompiler();
        compiler.Completion.SetResult(CompiledProviderLifetimeFixture.Compile(
            CompiledProviderLifetimeFixture.Compiler(custom: false), plan));
        using var provider = new CompiledExecutionProvider(compiler);
        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await CompiledProviderLifetimeFixture.Request(provider, plan, publish));
        Assert.Equal(0, compiler.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Disposal_preserves_admitted_compile_completion_and_original_errors(bool publish, bool fail)
    {
        using var host = SandboxHost.Create();
        var plan = CompiledProviderLifetimeFixture.Prepare(host);
        var compiler = new CompiledProviderLifetimeFixture.PendingCompiler();
        using var provider = new CompiledExecutionProvider(compiler);
        var pending = CompiledProviderLifetimeFixture.Request(provider, plan, publish).AsTask();
        Assert.Equal(1, compiler.Calls);
        Assert.False(pending.IsCompleted);
        provider.Dispose();
        if (fail)
        {
            var error = new InvalidOperationException("admitted compilation failed");
            compiler.Completion.SetException(error);
            Assert.Same(error, await Record.ExceptionAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(5))));
        }
        else
        {
            compiler.Completion.SetResult(CompiledProviderLifetimeFixture.Compile(
                CompiledProviderLifetimeFixture.Compiler(custom: false), plan));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.Equal(1, compiler.Calls);
    }
}
