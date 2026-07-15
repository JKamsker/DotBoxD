using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.ExecutionCache;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompiledExecutionProviderCacheHitTests
{
    private const int WarmupIterations = 5_000;
    private const int MeasuredIterations = 100_000;
    private const double MaximumBytesPerHit = 1D;

    [Fact]
    public async Task Warmed_default_reflection_emit_provider_hit_is_synchronous_and_near_zero_allocation()
    {
        var plan = await PreparePlanAsync();
        var verifier = new CountingGeneratedAssemblyVerifier();
        var compiler = new ReflectionEmitSandboxCompiler(verifier);
        using var provider = new CompiledExecutionProvider(compiler);

        var first = await provider.GetAsync(plan, "main", CancellationToken.None);
        Assert.Equal("Miss", first.MaterializationStatus);

        var completedHit = provider.GetAsync(plan, "main", CancellationToken.None);
        Assert.True(completedHit.IsCompletedSuccessfully);
        Assert.Equal("Hit", (await completedHit).MaterializationStatus);
        _ = MeasureCompletedHits(provider, plan, WarmupIterations);
        ForceGc();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var checksum = MeasureCompletedHits(provider, plan, MeasuredIterations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var bytesPerHit = allocated / (double)MeasuredIterations;

        Console.WriteLine(
            $"CompiledExecutionProvider completed hit: {allocated:N0} B; {bytesPerHit:N1} B/hit.");
        Assert.Equal(1, verifier.Calls);
        Assert.True(
            bytesPerHit <= MaximumBytesPerHit,
            $"expected completed provider hits to allocate at most {MaximumBytesPerHit:N1} B/hit; " +
            $"observed {bytesPerHit:N1} B/hit.");
        GC.KeepAlive(checksum);
    }

    [Fact]
    public async Task Precancelled_token_wins_over_warmed_provider_hit()
    {
        var plan = await PreparePlanAsync();
        var verifier = new CountingGeneratedAssemblyVerifier();
        var compiler = new ReflectionEmitSandboxCompiler(verifier);
        using var provider = new CompiledExecutionProvider(compiler);
        _ = await provider.GetAsync(plan, "main", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var cancelledHit = provider.GetAsync(plan, "main", cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledHit);

        var subsequentHit = provider.GetAsync(plan, "main", CancellationToken.None);
        Assert.True(subsequentHit.IsCompletedSuccessfully);
        Assert.Equal("Hit", (await subsequentHit).MaterializationStatus);
        Assert.Equal(1, verifier.Calls);
    }

    private static int MeasureCompletedHits(
        CompiledExecutionProvider provider,
        ExecutionPlan plan,
        int iterations)
    {
        var checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var pending = provider.GetAsync(plan, "main", CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("warmed provider hit unexpectedly became asynchronous");
            }

            var executable = pending.Result;
            if (!StringComparer.Ordinal.Equals(executable.MaterializationStatus, "Hit"))
            {
                throw new InvalidOperationException("warmed provider hit lost its materialization status");
            }

            checksum += executable.Artifact.ArtifactHash.Length;
        }

        return checksum;
    }

    private static async Task<ExecutionPlan> PreparePlanAsync()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class CountingGeneratedAssemblyVerifier : IGeneratedAssemblyVerifier
    {
        private readonly GeneratedAssemblyVerifier _inner = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<VerificationResult> VerifyAsync(
            ReadOnlyMemory<byte> assemblyBytes,
            ArtifactManifest manifest,
            VerificationPolicy policy,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _inner.VerifyAsync(assemblyBytes, manifest, policy, cancellationToken);
        }
    }
}
