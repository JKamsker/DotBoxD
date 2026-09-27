using System.Runtime.CompilerServices;
using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheLifetime;

public sealed class CompiledArtifactDisposalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_artifact_cache_rejects_new_work(bool compilerOverload)
    {
        using var host = SandboxHost.Create();
        var plan = CompiledProviderLifetimeFixture.Prepare(host);
        var compiler = new CompiledProviderLifetimeFixture.PendingCompiler();
        compiler.Completion.SetResult(CompiledProviderLifetimeFixture.Compile(
            CompiledProviderLifetimeFixture.Compiler(custom: false), plan));
        using var cache = new CompiledArtifactExecutionCache();
        cache.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await Request(cache, plan, compiler, compilerOverload));
        Assert.Equal(0, compiler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_artifact_cache_does_not_retain_late_success(bool compilerOverload)
    {
        using var host = SandboxHost.Create();
        var plan = CompiledProviderLifetimeFixture.Prepare(host);
        using var cache = new CompiledArtifactExecutionCache();
        var reference = CompleteAfterDisposal(cache, plan, compilerOverload);
        await CompiledProviderLifetimeFixture.AssertCollectedAsync([reference]);
        GC.KeepAlive(cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompleteAfterDisposal(
        CompiledArtifactExecutionCache cache, ExecutionPlan plan, bool compilerOverload)
    {
        var compiler = new CompiledProviderLifetimeFixture.PendingCompiler();
        var pending = Request(cache, plan, compiler, compilerOverload).AsTask();
        Assert.False(pending.IsCompleted);
        cache.Dispose();
        var artifact = CompiledProviderLifetimeFixture.Compile(
            CompiledProviderLifetimeFixture.Compiler(custom: false), plan);
        compiler.Completion.SetResult(artifact);
        Assert.Same(artifact, pending.GetAwaiter().GetResult());
        Assert.Equal(1, compiler.Calls);
        return new WeakReference(artifact);
    }

    private static ValueTask<CompiledArtifact> Request(
        CompiledArtifactExecutionCache cache, ExecutionPlan plan,
        CompiledProviderLifetimeFixture.PendingCompiler compiler, bool compilerOverload)
        => compilerOverload
            ? cache.GetAsync(plan, "main", compiler, CancellationToken.None)
            : cache.GetAsync(plan, "main", ct => compiler.CompileAsync(plan, new CompileOptions("main"), ct),
                CancellationToken.None);
}
