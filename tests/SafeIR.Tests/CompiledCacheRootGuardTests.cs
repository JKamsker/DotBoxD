using SafeIR.Compiler;

namespace SafeIR.Tests;

public sealed class CompiledCacheRootGuardTests
{
    [Fact]
    public void Persistent_cache_accepts_private_temp_root()
    {
        using var temp = TempDirectory.Create();

        var cache = new PersistentCompiledArtifactCache(temp.Path);

        Assert.False(cache.EntryExists(new string('0', 64)));
    }

    [Fact]
    public void Persistent_cache_rejects_group_or_world_writable_unix_root()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempDirectory.Create();
        File.SetUnixFileMode(
            temp.Path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupWrite);

        var ex = Assert.Throws<SandboxRuntimeException>(() => new PersistentCompiledArtifactCache(temp.Path));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-cache-root-" + Guid.NewGuid().ToString("N"));
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
