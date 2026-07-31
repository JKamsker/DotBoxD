using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;
using PersistentCompiledArtifactCache = DotBoxD.Kernels.Compiler.PersistentCompiledArtifactCache;

namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheIntegrity;

public sealed class PersistentCompiledCacheReplacementCleanupTests
{
    [Fact]
    public async Task Replacement_write_does_not_report_failure_after_new_entry_is_committed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        var plan = await PreparePlanAsync();
        var cache = new PersistentCompiledArtifactCache(temp.Path);
        var first = CreateWriteFixture(plan, value: 11);
        var second = CreateWriteFixture(plan, value: 22);
        Assert.Equal(first.CacheKey, second.CacheKey);
        await WriteAsync(cache, first);

        var entryPath = cache.EntryPath(first.CacheKey);
        // The stale payload traps only best-effort old-entry retirement after publish; it must not
        // affect validation of the replacement entry that has already been committed.
        var retiredPayload = Path.Combine(entryPath, "retired-payload");
        Directory.CreateDirectory(retiredPayload);
        File.SetUnixFileMode(retiredPayload, UnixFileMode.None);
        Exception? writeException = null;
        try
        {
            writeException = await Record.ExceptionAsync(
                async () => await WriteAsync(cache, second));
        }
        finally
        {
            RestoreDirectoryTreeAccess(entryPath);
            foreach (var oldEntry in Directory.EnumerateDirectories(temp.Path, ".old-*"))
            {
                RestoreDirectoryTreeAccess(oldEntry);
            }
        }

        var lookup = await cache.TryReadAsync(
            second.CacheKey,
            plan,
            "main",
            new GeneratedAssemblyVerifier(),
            second.Policy,
            CancellationToken.None);

        Assert.Equal(CompiledCacheStatus.Hit, lookup.Status);
        var artifact = Assert.IsType<CompiledArtifact>(lookup.Artifact);
        Assert.Equal(second.Verification.AssemblyHash, artifact.Verification.AssemblyHash);
        Assert.Equal(second.AssemblyBytes, artifact.AssemblyBytes);
        Assert.Null(writeException);
    }

    private static async Task<ExecutionPlan> PreparePlanAsync()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static CacheWriteFixture CreateWriteFixture(ExecutionPlan plan, int value)
    {
        var assemblyBytes = CompiledArtifactTestFactory.BuildI32Assembly(parameterCount: 2, value);
        var artifact = CompiledArtifactTestFactory.LoadedAssembly(plan, assemblyBytes);
        return new CacheWriteFixture(
            plan,
            artifact.Manifest.CacheKey,
            assemblyBytes,
            artifact.Manifest,
            artifact.Verification,
            VerificationPolicy.BoxedValueDefaults());
    }

    private static async ValueTask WriteAsync(
        PersistentCompiledArtifactCache cache,
        CacheWriteFixture fixture)
        => await cache.WriteAsync(
            fixture.CacheKey,
            fixture.Plan,
            "main",
            fixture.AssemblyBytes,
            fixture.Manifest,
            fixture.Verification,
            fixture.Policy,
            CancellationToken.None);

    private static void RestoreDirectoryTreeAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (Directory.Exists(path))
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }

        foreach (var child in Directory.Exists(path)
            ? Directory.EnumerateDirectories(path)
            : [])
        {
            RestoreDirectoryTreeAccess(child);
        }
    }

    private sealed record CacheWriteFixture(
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
                "dotboxd-cache-replacement-" + Guid.NewGuid().ToString("N"));
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
