using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Plugin.Tests.Debugging;

public sealed class GameServerDebugMetadataTests
{
    [Fact]
    public void Guardian_maps_predicate_and_handler_source_lines()
    {
        var package = KernelPackageRegistry.Resolve<GuardianKernel>();
        var debugInfo = Assert.IsType<KernelDebugInfo>(package.DebugInfo);
        var nodes = SandboxNodeMap.Create(package.Module).Nodes.ToDictionary(node => node.Id);

        Assert.Contains(35, Lines(package.Entrypoints.ShouldHandle));
        Assert.Contains(44, Lines(package.Entrypoints.Handle));

        IReadOnlySet<int> Lines(string functionId) => debugInfo.SequencePoints
            .Where(point => nodes[point.NodeId].FunctionId == functionId)
            .Select(point => point.Span.Line)
            .ToHashSet();
    }
}
