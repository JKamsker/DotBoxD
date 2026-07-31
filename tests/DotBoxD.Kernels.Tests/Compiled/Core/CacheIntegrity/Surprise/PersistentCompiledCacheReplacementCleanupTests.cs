using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Compiler.Internal;
using DotBoxD.Kernels.Compiler.Internal.CacheIntegrity;
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
    public void Publisher_moves_file_entries_as_previous_cache_entries()
    {
        using var temp = TempDirectory.Create();
        var finalPath = Path.Combine(temp.Path, "entry.dll");
        var previousPath = Path.Combine(temp.Path, "old-entry.dll");
        File.WriteAllText(finalPath, "cached");

        var moved = PersistentCompiledArtifactCachePublisher.MoveExistingEntryAside(finalPath, previousPath);

        Assert.True(moved);
        Assert.False(File.Exists(finalPath));
        Assert.Equal("cached", File.ReadAllText(previousPath));

        PersistentCompiledArtifactCachePublisher.RestorePreviousEntry(finalPath, previousPath, movedPrevious: true);

        Assert.Equal("cached", File.ReadAllText(finalPath));
        Assert.False(File.Exists(previousPath));
    }

    [Fact]
    public void Publisher_leaves_missing_entries_unmoved()
    {
        using var temp = TempDirectory.Create();
        var finalPath = Path.Combine(temp.Path, "missing-entry");
        var previousPath = Path.Combine(temp.Path, "old-missing-entry");

        var moved = PersistentCompiledArtifactCachePublisher.MoveExistingEntryAside(finalPath, previousPath);
        PersistentCompiledArtifactCachePublisher.RestorePreviousEntry(finalPath, previousPath, moved);

        Assert.False(moved);
        Assert.False(File.Exists(finalPath));
        Assert.False(Directory.Exists(finalPath));
        Assert.False(File.Exists(previousPath));
        Assert.False(Directory.Exists(previousPath));
    }

    [Fact]
    public void Publisher_restores_directory_entries_as_previous_cache_entries()
    {
        using var temp = TempDirectory.Create();
        var finalPath = Path.Combine(temp.Path, "entry");
        var previousPath = Path.Combine(temp.Path, "old-entry");
        Directory.CreateDirectory(previousPath);
        File.WriteAllText(Path.Combine(previousPath, "module.dll"), "cached");

        PersistentCompiledArtifactCachePublisher.RestorePreviousEntry(finalPath, previousPath, movedPrevious: true);

        Assert.True(Directory.Exists(finalPath));
        Assert.Equal("cached", File.ReadAllText(Path.Combine(finalPath, "module.dll")));
        Assert.False(Directory.Exists(previousPath));
    }

    [Fact]
    public void Publisher_deletes_supported_entry_shapes()
    {
        using var temp = TempDirectory.Create();
        var filePath = Path.Combine(temp.Path, "entry.dll");
        var directoryPath = Path.Combine(temp.Path, "entry");
        var nestedPath = Path.Combine(directoryPath, "module.dll");
        File.WriteAllText(filePath, "cached");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(nestedPath, "cached");

        PersistentCompiledArtifactCachePublisher.DeleteEntryIfExists(filePath);
        PersistentCompiledArtifactCachePublisher.DeleteEntryIfExists(directoryPath);
        PersistentCompiledArtifactCachePublisher.DeleteEntryIfExists(Path.Combine(temp.Path, "missing"));

        Assert.False(File.Exists(filePath));
        Assert.False(Directory.Exists(directoryPath));
    }

    [Fact]
    public void Publisher_rejects_malformed_entry_shapes()
    {
        using var temp = TempDirectory.Create();
        var entryPath = Path.Combine(temp.Path, "entry");
        Directory.CreateDirectory(entryPath);

        var incomplete = Assert.Throws<IOException>(
            () => PersistentCompiledArtifactCachePublisher.ValidateEntryShape(entryPath));

        File.WriteAllText(Path.Combine(entryPath, "module.dll"), "cached");
        File.WriteAllText(Path.Combine(entryPath, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(entryPath, "verification.json"), "{}");
        File.WriteAllText(Path.Combine(entryPath, PersistentCompiledArtifactCacheOrigin.ProofFileName), "proof");
        File.WriteAllText(Path.Combine(entryPath, "extra.bin"), "extra");
        var unexpected = Assert.Throws<IOException>(
            () => PersistentCompiledArtifactCachePublisher.ValidateEntryShape(entryPath));

        Assert.Equal("compiled cache entry is incomplete", incomplete.Message);
        Assert.Equal("compiled cache entry contains unexpected file", unexpected.Message);
    }

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
