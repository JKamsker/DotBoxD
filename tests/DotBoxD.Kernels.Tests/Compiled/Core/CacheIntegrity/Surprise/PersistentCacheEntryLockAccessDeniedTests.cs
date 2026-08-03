using DotBoxD.Kernels.Compiler.Internal;

namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheIntegrity;

public sealed class PersistentCacheEntryLockAccessDeniedTests
{
    [Fact]
    public async Task Permanent_lock_path_access_denial_is_not_reported_as_cancellation()
    {
        using var temp = TempDirectory.Create();
        var cacheKey = new string('a', 64);
        Directory.CreateDirectory(LockPath(temp.Path, cacheKey));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var entryLock = await PersistentCacheEntryLock.AcquireAsync(
                temp.Path,
                cacheKey,
                cancellation.Token);
        });

        Assert.IsType<UnauthorizedAccessException>(exception);
    }

    private static string LockPath(string rootDirectory, string cacheKey)
        => Path.Combine(rootDirectory, ".locks", cacheKey[..2], cacheKey[2..4], cacheKey + ".lock");

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-lock-access-denied-" + Guid.NewGuid().ToString("N"));
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
