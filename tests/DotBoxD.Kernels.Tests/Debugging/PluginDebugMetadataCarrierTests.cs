using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Debugging;

public sealed class PluginDebugMetadataCarrierTests
{
    [Fact]
    public void Generated_named_kernel_maps_should_handle_handle_and_source_variables()
    {
        var package = FireDamagePluginPackage.Create();

        var debugInfo = Assert.IsType<KernelDebugInfo>(package.DebugInfo);
        Assert.Equal(2, debugInfo.Documents.Count);
        Assert.All(debugInfo.Documents, document =>
        {
            Assert.EndsWith("FireDamageKernel.cs", document.Path, StringComparison.Ordinal);
            Assert.True(document.MatchesSource(File.ReadAllText(document.Path)));
        });
        var nodes = SandboxNodeMap.Create(package.Module).Nodes.ToDictionary(node => node.Id);
        var mappedFunctions = debugInfo.SequencePoints
            .Select(point => nodes[point.NodeId].FunctionId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(package.Entrypoints.ShouldHandle, mappedFunctions);
        Assert.Contains(package.Entrypoints.Handle, mappedFunctions);
        Assert.True(DistinctLocations(debugInfo, nodes, package.Entrypoints.ShouldHandle) > 1);
        Assert.True(DistinctLocations(debugInfo, nodes, package.Entrypoints.Handle) > 1);
        Assert.Contains(debugInfo.VariableBindings, binding =>
            binding.FunctionId == package.Entrypoints.ShouldHandle &&
            binding.SourceName == "e.Amount");
        Assert.Contains(debugInfo.VariableBindings, binding =>
            binding.FunctionId == package.Entrypoints.Handle &&
            binding.SourceName == "e.TargetId");
    }

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

    private static int DistinctLocations(
        KernelDebugInfo debugInfo,
        IReadOnlyDictionary<SandboxNodeId, SandboxNodeDescriptor> nodes,
        string functionId)
        => debugInfo.SequencePoints
            .Where(point => nodes[point.NodeId].FunctionId == functionId)
            .Select(point => (
                point.Span.DocumentId,
                point.Span.Line,
                point.Span.Column,
                point.Span.EndLine,
                point.Span.EndColumn))
            .Distinct()
            .Count();

}
