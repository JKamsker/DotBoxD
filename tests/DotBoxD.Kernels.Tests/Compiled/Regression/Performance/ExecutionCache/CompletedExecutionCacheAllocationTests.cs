using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.ExecutionCache;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompletedExecutionCacheAllocationTests
{
    private const int WarmupIterations = 5_000;
    private const int MeasuredIterations = 100_000;
    private const double MaximumBytesPerHit = 1D;
    private static readonly Func<CancellationToken, ValueTask<CompiledArtifact>> UnexpectedCompile =
        static _ => throw new InvalidOperationException("completed artifact hit invoked its compile factory");
    private static readonly Func<CancellationToken, ValueTask<CompiledExecutable>> UnexpectedMaterialization =
        static _ => throw new InvalidOperationException("completed executable hit invoked its materialization factory");

    [Fact]
    public async Task Artifact_cache_completed_hit_is_synchronous_and_near_zero_allocation()
    {
        var (plan, artifact) = await CreatePlanAndArtifactAsync();
        var cache = new CompiledArtifactExecutionCache();
        _ = await cache.GetAsync(
            plan,
            "main",
            _ => ValueTask.FromResult(artifact),
            CancellationToken.None);

        _ = MeasureArtifactHits(cache, plan, WarmupIterations);
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = MeasureArtifactHits(cache, plan, MeasuredIterations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        AssertNearZero("artifact execution cache", allocated);
        GC.KeepAlive(checksum);
    }

    [Fact]
    public async Task Executable_cache_completed_hit_is_synchronous_and_near_zero_allocation()
    {
        var (plan, artifact) = await CreatePlanAndArtifactAsync();
        var expected = new CompiledExecutable(artifact, "Miss");
        var cache = new CompiledExecutableExecutionCache();
        _ = await cache.GetAsync(
            plan,
            "main",
            _ => ValueTask.FromResult(expected),
            CancellationToken.None);

        _ = MeasureExecutableHits(cache, plan, WarmupIterations);
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = MeasureExecutableHits(cache, plan, MeasuredIterations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        AssertNearZero("executable execution cache", allocated);
        GC.KeepAlive(checksum);
    }

    [Fact]
    public async Task Artifact_completed_lookup_refreshes_lru_recency()
    {
        var (plan, artifact) = await CreatePlanAndArtifactAsync();
        var cache = new CompiledArtifactExecutionCache();
        Func<CancellationToken, ValueTask<CompiledArtifact>> compile = _ => ValueTask.FromResult(artifact);
        for (var i = 0; i < 64; i++)
        {
            _ = await cache.GetAsync(plan, Entrypoint(i), compile, CancellationToken.None);
        }

        var touched = cache.GetAsync(plan, Entrypoint(0), UnexpectedCompile, CancellationToken.None);
        Assert.True(touched.IsCompletedSuccessfully);
        Assert.Same(artifact, await touched);
        _ = await cache.GetAsync(plan, Entrypoint(64), compile, CancellationToken.None);

        var retained = cache.GetAsync(plan, Entrypoint(0), UnexpectedCompile, CancellationToken.None);
        Assert.True(retained.IsCompletedSuccessfully);
        Assert.Same(artifact, await retained);
        var evictedFactoryCalls = 0;
        _ = await cache.GetAsync(
            plan,
            Entrypoint(1),
            _ =>
            {
                evictedFactoryCalls++;
                return ValueTask.FromResult(artifact);
            },
            CancellationToken.None);
        Assert.Equal(1, evictedFactoryCalls);
    }

    [Fact]
    public async Task Executable_completed_lookup_refreshes_lru_recency()
    {
        var (plan, artifact) = await CreatePlanAndArtifactAsync();
        var expected = new CompiledExecutable(artifact, "Miss");
        var cache = new CompiledExecutableExecutionCache();
        Func<CancellationToken, ValueTask<CompiledExecutable>> materialize =
            _ => ValueTask.FromResult(expected);
        for (var i = 0; i < 64; i++)
        {
            _ = await cache.GetAsync(plan, Entrypoint(i), materialize, CancellationToken.None);
        }

        var touched = cache.GetAsync(plan, Entrypoint(0), UnexpectedMaterialization, CancellationToken.None);
        Assert.True(touched.IsCompletedSuccessfully);
        Assert.Equal("Hit", (await touched).MaterializationStatus);
        _ = await cache.GetAsync(plan, Entrypoint(64), materialize, CancellationToken.None);

        var retained = cache.GetAsync(plan, Entrypoint(0), UnexpectedMaterialization, CancellationToken.None);
        Assert.True(retained.IsCompletedSuccessfully);
        Assert.Equal("Hit", (await retained).MaterializationStatus);
        var evictedFactoryCalls = 0;
        _ = await cache.GetAsync(
            plan,
            Entrypoint(1),
            _ =>
            {
                evictedFactoryCalls++;
                return ValueTask.FromResult(expected);
            },
            CancellationToken.None);
        Assert.Equal(1, evictedFactoryCalls);
    }

    private static int MeasureArtifactHits(
        CompiledArtifactExecutionCache cache,
        ExecutionPlan plan,
        int iterations)
    {
        var checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var pending = cache.GetAsync(plan, "main", UnexpectedCompile, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("completed artifact cache hit unexpectedly became asynchronous");
            }

            checksum += pending.Result.ArtifactHash.Length;
        }

        return checksum;
    }

    private static int MeasureExecutableHits(
        CompiledExecutableExecutionCache cache,
        ExecutionPlan plan,
        int iterations)
    {
        var checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var pending = cache.GetAsync(plan, "main", UnexpectedMaterialization, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("completed executable cache hit unexpectedly became asynchronous");
            }

            var executable = pending.Result;
            if (!StringComparer.Ordinal.Equals(executable.MaterializationStatus, "Hit"))
            {
                throw new InvalidOperationException("completed executable cache hit lost its hit status");
            }

            checksum += executable.Artifact.ArtifactHash.Length;
        }

        return checksum;
    }

    private static void AssertNearZero(string name, long allocated)
    {
        var bytesPerHit = allocated / (double)MeasuredIterations;
        Console.WriteLine($"{name} completed hit: {allocated:N0} B; {bytesPerHit:N1} B/hit.");
        Assert.True(
            bytesPerHit <= MaximumBytesPerHit,
            $"expected {name} hits to allocate at most {MaximumBytesPerHit:N1} B/hit; " +
            $"observed {bytesPerHit:N1} B/hit.");
    }

    private static async Task<(ExecutionPlan Plan, CompiledArtifact Artifact)> CreatePlanAndArtifactAsync()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var artifact = CompiledArtifactTestFactory.DynamicMethod(
            plan,
            static (_, _) => SandboxValue.Unit);
        return (plan, artifact);
    }

    private static string Entrypoint(int index)
        => $"entry-{index}";

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
