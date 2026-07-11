using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;
using PersistentCompiledArtifactCache = DotBoxD.Kernels.Compiler.PersistentCompiledArtifactCache;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ArgumentValidation;

public sealed class CompiledCacheArgumentValidationTests
{
    [Fact]
    public void Constructor_rejects_null_root_directory_with_public_parameter_name()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new PersistentCompiledArtifactCache(null!));

        Assert.Equal("rootDirectory", ex.ParamName);
    }

    [Fact]
    public void EntryPath_rejects_null_cache_key_with_public_parameter_name()
    {
        using var temp = TempDirectory.Create();
        var cache = new PersistentCompiledArtifactCache(temp.Path);

        var ex = Assert.Throws<ArgumentNullException>(() => cache.EntryPath(null!));

        Assert.Equal("cacheKey", ex.ParamName);
    }

    [Fact]
    public void EntryExists_rejects_null_cache_key_with_public_parameter_name()
    {
        using var temp = TempDirectory.Create();
        var cache = new PersistentCompiledArtifactCache(temp.Path);

        var ex = Assert.Throws<ArgumentNullException>(() => cache.EntryExists(null!));

        Assert.Equal("cacheKey", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(TryReadNullCollaborators))]
    public async Task TryReadAsync_rejects_null_collaborators_before_cache_miss(
        string parameterName,
        Func<PersistentCompiledArtifactCache, string, ExecutionPlan, VerificationPolicy, ValueTask<CompiledCacheLookup>> act)
    {
        using var temp = TempDirectory.Create();
        var plan = await PreparePlanAsync();
        var policy = VerificationPolicy.BoxedValueDefaults();
        var cacheKey = CacheKeyBuilder.Build(plan, "main", policy, optimize: false);
        var cache = new PersistentCompiledArtifactCache(temp.Path);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await act(cache, cacheKey, plan, policy).AsTask());

        Assert.Equal(parameterName, ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(WriteNullCollaborators))]
    public async Task WriteAsync_rejects_null_collaborators_before_validation_helpers(
        string parameterName,
        Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask> act)
    {
        using var temp = TempDirectory.Create();
        var plan = await PreparePlanAsync();
        var fixture = CreateWriteFixture(plan);
        var cache = new PersistentCompiledArtifactCache(temp.Path);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await act(cache, fixture.CacheKey, fixture).AsTask());

        Assert.Equal(parameterName, ex.ParamName);
    }

    public static IEnumerable<object[]> TryReadNullCollaborators()
    {
        yield return
        [
            "plan",
            new Func<PersistentCompiledArtifactCache, string, ExecutionPlan, VerificationPolicy, ValueTask<CompiledCacheLookup>>(
                (cache, cacheKey, _, policy) => cache.TryReadAsync(
                    cacheKey,
                    null!,
                    "main",
                    new GeneratedAssemblyVerifier(),
                    policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "entrypoint",
            new Func<PersistentCompiledArtifactCache, string, ExecutionPlan, VerificationPolicy, ValueTask<CompiledCacheLookup>>(
                (cache, cacheKey, plan, policy) => cache.TryReadAsync(
                    cacheKey,
                    plan,
                    null!,
                    new GeneratedAssemblyVerifier(),
                    policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "verifier",
            new Func<PersistentCompiledArtifactCache, string, ExecutionPlan, VerificationPolicy, ValueTask<CompiledCacheLookup>>(
                (cache, cacheKey, plan, policy) => cache.TryReadAsync(
                    cacheKey,
                    plan,
                    "main",
                    null!,
                    policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "policy",
            new Func<PersistentCompiledArtifactCache, string, ExecutionPlan, VerificationPolicy, ValueTask<CompiledCacheLookup>>(
                (cache, cacheKey, plan, _) => cache.TryReadAsync(
                    cacheKey,
                    plan,
                    "main",
                    new GeneratedAssemblyVerifier(),
                    null!,
                    CancellationToken.None))
        ];
    }

    public static IEnumerable<object[]> WriteNullCollaborators()
    {
        yield return
        [
            "plan",
            new Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask>(
                (cache, cacheKey, fixture) => cache.WriteAsync(
                    cacheKey,
                    null!,
                    "main",
                    fixture.AssemblyBytes,
                    fixture.Manifest,
                    fixture.Verification,
                    fixture.Policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "entrypoint",
            new Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask>(
                (cache, cacheKey, fixture) => cache.WriteAsync(
                    cacheKey,
                    fixture.Plan,
                    null!,
                    fixture.AssemblyBytes,
                    fixture.Manifest,
                    fixture.Verification,
                    fixture.Policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "assemblyBytes",
            new Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask>(
                (cache, cacheKey, fixture) => cache.WriteAsync(
                    cacheKey,
                    fixture.Plan,
                    "main",
                    null!,
                    fixture.Manifest,
                    fixture.Verification,
                    fixture.Policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "manifest",
            new Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask>(
                (cache, cacheKey, fixture) => cache.WriteAsync(
                    cacheKey,
                    fixture.Plan,
                    "main",
                    fixture.AssemblyBytes,
                    null!,
                    fixture.Verification,
                    fixture.Policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "verification",
            new Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask>(
                (cache, cacheKey, fixture) => cache.WriteAsync(
                    cacheKey,
                    fixture.Plan,
                    "main",
                    fixture.AssemblyBytes,
                    fixture.Manifest,
                    null!,
                    fixture.Policy,
                    CancellationToken.None))
        ];
        yield return
        [
            "policy",
            new Func<PersistentCompiledArtifactCache, string, CacheWriteFixture, ValueTask>(
                (cache, cacheKey, fixture) => cache.WriteAsync(
                    cacheKey,
                    fixture.Plan,
                    "main",
                    fixture.AssemblyBytes,
                    fixture.Manifest,
                    fixture.Verification,
                    null!,
                    CancellationToken.None))
        ];
    }

    private static async Task<ExecutionPlan> PreparePlanAsync()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static CacheWriteFixture CreateWriteFixture(ExecutionPlan plan)
    {
        var assemblyBytes = CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value: 45);
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(plan, assemblyBytes);
        var policy = VerificationPolicy.BoxedValueDefaults();
        return new CacheWriteFixture(
            plan,
            artifact.Manifest.CacheKey,
            assemblyBytes,
            artifact.Manifest,
            artifact.Verification,
            policy);
    }

    public sealed record CacheWriteFixture(
        ExecutionPlan Plan,
        string CacheKey,
        byte[] AssemblyBytes,
        ArtifactManifest Manifest,
        VerificationResult Verification,
        VerificationPolicy Policy);

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-cache-args-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
