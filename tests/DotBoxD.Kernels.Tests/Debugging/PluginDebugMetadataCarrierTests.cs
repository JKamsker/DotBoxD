using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Debugging;

public sealed class PluginDebugMetadataCarrierTests
{
    [Fact]
    public void Package_and_ir_kernel_preserve_client_only_debug_info()
    {
        var generated = FireDamagePluginPackage.Create();
        var document = KernelDebugDocument.FromSource("plugin", "/mapped/Plugin.cs", "return 7;");
        var debugInfo = new KernelDebugInfo([document], []);

        var package = PluginPackage.Create(
            generated.Manifest,
            generated.Module,
            generated.Entrypoints,
            debugInfo);
        var kernel = IRKernel.FromPackage(package);

        Assert.Same(debugInfo, package.DebugInfo);
        Assert.Same(debugInfo, kernel.DebugInfo);
    }

    [Fact]
    public void Package_json_excludes_debug_documents_checksums_and_mappings()
    {
        var generated = FireDamagePluginPackage.Create();
        var document = KernelDebugDocument.FromSource("plugin", "/mapped/SecretPlugin.cs", "return 7;");
        var package = PluginPackage.Create(
            generated.Manifest,
            generated.Module,
            generated.Entrypoints,
            new KernelDebugInfo([document], []));

        var json = PluginPackageJsonSerializer.Export(package);
        var imported = PluginPackageJsonSerializer.Import(json);

        Assert.DoesNotContain("SecretPlugin", json, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Sha256Checksum, json, StringComparison.Ordinal);
        Assert.DoesNotContain("debug", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(imported.DebugInfo);
        Assert.Equal(CanonicalModuleHasher.Hash(package.Module), CanonicalModuleHasher.Hash(imported.Module));
    }

}
