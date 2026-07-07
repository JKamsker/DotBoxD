using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ArgumentValidation;

public sealed class CompiledCacheLookupArgumentValidationTests
{
    [Fact]
    public void Constructor_rejects_unknown_status()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompiledCacheLookup((CompiledCacheStatus)99, null));

        Assert.Equal(nameof(CompiledCacheLookup.Status), ex.ParamName);
    }

    [Fact]
    public void Hit_rejects_null_artifact()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(
            () => new CompiledCacheLookup(CompiledCacheStatus.Hit, null));

        Assert.Equal(nameof(CompiledCacheLookup.Artifact), ex.ParamName);
    }

    [Fact]
    public void Miss_rejects_artifact()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentException>(
            () => new CompiledCacheLookup(CompiledCacheStatus.Miss, artifact));

        Assert.Equal(nameof(CompiledCacheLookup.Artifact), ex.ParamName);
    }

    [Fact]
    public void Miss_rejects_invalid_reason()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new CompiledCacheLookup(CompiledCacheStatus.Miss, null, "InvalidJson"));

        Assert.Equal(nameof(CompiledCacheLookup.InvalidReason), ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_rejects_missing_invalid_reason(string? invalidReason)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new CompiledCacheLookup(CompiledCacheStatus.Invalid, null, invalidReason));

        Assert.Equal(nameof(CompiledCacheLookup.InvalidReason), ex.ParamName);
    }
}
