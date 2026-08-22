using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Compiled.Core.CacheIntegrity;

public sealed class SandboxHostCreationAtomicityTests
{
    [Fact]
    public void Failed_creation_does_not_create_a_previously_missing_compiler_cache_directory()
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "dotboxd-host-creation-atomicity-" + Guid.NewGuid().ToString("N"));
        var cacheDirectory = Path.Combine(rootDirectory, "cache");

        try
        {
            Assert.False(Directory.Exists(cacheDirectory));

            _ = Assert.Throws<SandboxValidationException>(() => SandboxHost.Create(builder =>
            {
                builder.UseCompilerIfAvailable();
                builder.UseCompilerCache(cacheDirectory);
                builder.AddDefaultPureBindings();
                builder.AddDefaultPureBindings();
            }));

            Assert.False(Directory.Exists(cacheDirectory));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }
}
