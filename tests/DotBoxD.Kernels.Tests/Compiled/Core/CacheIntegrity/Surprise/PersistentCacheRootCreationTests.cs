using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class PersistentCacheRootCreationTests
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewUnixCacheRoot_IsCreatedWithOwnerOnlyPermissions(bool nested)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Directory.CreateTempSubdirectory("dotboxd-cache-creation-");
        try
        {
            var path = nested
                ? Path.Combine(parent.FullName, "nested", "cache")
                : Path.Combine(parent.FullName, "cache");

            var cache = new PersistentCompiledArtifactCache(path);

            Assert.Equal(OwnerOnly, File.GetUnixFileMode(path));
            Assert.False(cache.EntryExists(new string('0', 64)));
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingUnixCacheRoot_KeepsItsPermittedMode(bool groupReadable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("dotboxd-cache-existing-");
        try
        {
            var mode = groupReadable ? OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute : OwnerOnly;
            File.SetUnixFileMode(root.FullName, mode);

            var cache = new PersistentCompiledArtifactCache(root.FullName);

            Assert.Equal(mode, File.GetUnixFileMode(root.FullName));
            Assert.False(cache.EntryExists(new string('0', 64)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
