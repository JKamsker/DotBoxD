using DotBoxD.Kernels.Compiler;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledArtifactArgumentValidationTests
{
    [Fact]
    public void Compiled_artifact_init_rejects_null_manifest()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { Manifest = null! });

        Assert.Equal(nameof(CompiledArtifact.Manifest), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_null_verification()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { Verification = null! });

        Assert.Equal(nameof(CompiledArtifact.Verification), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_null_assembly_hash()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { AssemblyHash = null! });

        Assert.Equal(nameof(CompiledArtifact.AssemblyHash), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_null_entrypoint()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { Entrypoint = null! });

        Assert.Equal(nameof(CompiledArtifact.Entrypoint), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_constructor_rejects_unknown_cache_status()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new CompiledArtifact(
            artifact.AssemblyBytes,
            artifact.AssemblyHash,
            artifact.Manifest,
            artifact.Verification,
            artifact.Entrypoint,
            artifact.RuntimeForm,
            (CompiledCacheStatus)99));

        Assert.Equal("cacheStatus", ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_unknown_runtime_form()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => artifact with { RuntimeForm = (CompiledRuntimeFormKind)99 });

        Assert.Equal(nameof(CompiledArtifact.RuntimeForm), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_unknown_cache_status()
    {
        var artifact = CompiledArtifactArgumentValidationFixtures.ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => artifact with { CacheStatus = (CompiledCacheStatus)99 });

        Assert.Equal(nameof(CompiledArtifact.CacheStatus), ex.ParamName);
    }
}
