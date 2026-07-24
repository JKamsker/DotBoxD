using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;
using DotBoxD.Kernels.Tests.Policy;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.ExecutionCache;

public sealed class CompiledExecutionProviderHotEntryTests
{
    [Fact]
    public async Task Completed_shortcut_requires_exact_plan_reference_and_ordinal_entrypoint()
    {
        var plan = await PreparePlanAsync();
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        using var provider = new CompiledExecutionProvider(compiler);
        _ = await provider.GetAndPublishCompletedExecutableAsync(
            plan,
            "main",
            CancellationToken.None);

        Assert.True(provider.TryGetCompletedExecutable(plan, "main", out var completed));
        Assert.Equal("Hit", completed.MaterializationStatus);
        Assert.False(provider.TryGetCompletedExecutable(plan, "MAIN", out _));
        var clone = Clone(plan);
        Assert.Equal(plan.PlanHash, clone.PlanHash);
        Assert.NotSame(plan, clone);
        Assert.False(provider.TryGetCompletedExecutable(clone, "main", out _));

        var cloneLookup = await provider.GetAndPublishCompletedExecutableAsync(
            clone,
            "main",
            CancellationToken.None);

        Assert.Equal("Hit", cloneLookup.MaterializationStatus);
        Assert.False(provider.TryGetCompletedExecutable(clone, "main", out _));
        Assert.True(provider.TryGetCompletedExecutable(plan, "main", out _));
        _ = await provider.GetAndPublishCompletedExecutableAsync(
            plan,
            "main",
            CancellationToken.None);
        Assert.True(provider.HasHotExecutableFor(plan, "main"));
    }

    [Fact]
    public async Task Completed_shortcut_stays_bounded_after_two_exact_plans()
    {
        var firstPlan = await PreparePlanAsync("completed-hot-provider-first");
        var secondPlan = await PreparePlanAsync("completed-hot-provider-second");
        var thirdPlan = await PreparePlanAsync("completed-hot-provider-third");
        using var provider = new CompiledExecutionProvider(
            new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier()));

        _ = await provider.GetAndPublishCompletedExecutableAsync(
            firstPlan,
            "main",
            CancellationToken.None);
        _ = await provider.GetAndPublishCompletedExecutableAsync(
            secondPlan,
            "main",
            CancellationToken.None);
        Assert.False(provider.CanPublishCompletedExecutable);
        _ = await provider.GetAndPublishCompletedExecutableAsync(
            thirdPlan,
            "main",
            CancellationToken.None);

        Assert.True(provider.HasHotExecutableFor(firstPlan, "main"));
        Assert.True(provider.HasHotExecutableFor(secondPlan, "main"));
        Assert.False(provider.HasHotExecutableFor(thirdPlan, "main"));
        Assert.True(provider.TryGetCachedCompletedExecutable(thirdPlan, "main", out var completed));
        Assert.Equal("Hit", completed.MaterializationStatus);
        Assert.False(provider.TryGetCachedCompletedExecutable(thirdPlan, "MAIN", out _));
        Assert.False(provider.TryGetCachedCompletedExecutable(Clone(thirdPlan), "main", out _));
    }

    [Fact]
    public async Task Custom_provider_revalidates_each_current_artifact_and_never_publishes_hot()
    {
        var plan = await PreparePlanAsync();
        var compiler = new MutatingCompiler();
        using var provider = new CompiledExecutionProvider(compiler);

        var first = await provider.GetAsync(plan, "main", CancellationToken.None);
        var error = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await provider.GetAsync(plan, "main", CancellationToken.None));

        Assert.Equal("Miss", first.MaterializationStatus);
        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.Equal(2, compiler.Calls);
        Assert.False(provider.TryGetCompletedExecutable(plan, "main", out _));
    }

    [Fact]
    public async Task Persistent_reflection_provider_runs_the_cache_pipeline_without_publishing_hot()
    {
        using var temp = PolicyMutationTestSupport.TempDirectory.Create();
        var plan = await PreparePlanAsync();
        var cache = new PersistentCompiledArtifactCache(temp.Path);
        var compiler = new ReflectionEmitSandboxCompiler(
            new GeneratedAssemblyVerifier(),
            cache: cache);
        using var provider = new CompiledExecutionProvider(compiler);

        var first = await provider.GetAsync(plan, "main", CancellationToken.None);
        var second = await provider.GetAsync(plan, "main", CancellationToken.None);

        Assert.Equal("Miss", first.MaterializationStatus);
        Assert.Equal("Hit", second.MaterializationStatus);
        Assert.Equal(CompiledCacheStatus.Hit, second.Artifact.CacheStatus);
        Assert.False(provider.TryGetCompletedExecutable(plan, "main", out _));
    }

    [Fact]
    public async Task Dispose_clears_completed_shortcut_and_rejects_late_provider_access()
    {
        var plan = await PreparePlanAsync();
        var provider = new CompiledExecutionProvider(
            new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier()));
        _ = await provider.GetAndPublishCompletedExecutableAsync(
            plan,
            "main",
            CancellationToken.None);
        Assert.True(provider.HasHotExecutableFor(plan, "main"));

        provider.Dispose();

        Assert.False(provider.HasHotExecutableFor(plan, "main"));
        Assert.False(provider.TryGetCompletedExecutable(plan, "main", out _));
        Assert.False(provider.TryGetCachedCompletedExecutable(plan, "main", out _));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await provider.GetAsync(plan, "main", CancellationToken.None));
        provider.Dispose();
    }

    [Fact]
    public async Task Dispose_during_custom_compile_preserves_the_admitted_compiler_reference()
    {
        var plan = await PreparePlanAsync();
        var compiler = new PendingCompiler();
        using var provider = new CompiledExecutionProvider(compiler);
        var pending = provider.GetAsync(plan, "main", CancellationToken.None).AsTask();
        await compiler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        provider.Dispose();
        compiler.Complete(CompiledArtifactTestFactory.DynamicMethod(
            plan,
            static (_, _) => SandboxValue.FromInt32(35)));

        _ = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);
        Assert.Equal(1, compiler.Calls);
    }

    private static ExecutionPlan Clone(ExecutionPlan plan)
        => new(
            plan.ModuleHash,
            plan.PlanHash,
            plan.PlanSeal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            plan.Module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            plan.BindingReferences);

    private static async Task<ExecutionPlan> PreparePlanAsync(
        string moduleId = "completed-hot-provider")
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson(moduleId));
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private sealed class MutatingCompiler : ISandboxCompiler
    {
        private CompiledArtifact? _artifact;

        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            _artifact ??= CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 35));
            if (Calls == 1)
            {
                return ValueTask.FromResult(_artifact);
            }

            var bytes = _artifact.AssemblyBytes.ToArray();
            bytes[0] ^= 0xff;
            return ValueTask.FromResult(_artifact with { AssemblyBytes = bytes });
        }
    }

    private sealed class PendingCompiler : ISandboxCompiler
    {
        private readonly TaskCompletionSource<CompiledArtifact> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            return new ValueTask<CompiledArtifact>(_completion.Task);
        }

        public void Complete(CompiledArtifact artifact) => _completion.SetResult(artifact);
    }
}
