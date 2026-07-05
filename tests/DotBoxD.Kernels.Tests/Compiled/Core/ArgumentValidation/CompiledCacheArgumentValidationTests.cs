using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

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
