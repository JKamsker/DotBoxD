using System.Text;
using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class PluginDebugSourceCatalogTests
{
    [Fact]
    public async Task Resolves_sequence_points_from_every_debug_document_for_the_same_source_path()
    {
        const string path = "/source/Kernel.cs";
        const string source = "predicate();\nhandler();";
        var package = FireDamagePluginPackage.Create();
        var nodes = SandboxNodeMap.Create(package.Module).Nodes;
        var sameFunctionNode = nodes.First(node =>
            node.Id != nodes[0].Id && node.FunctionId == nodes[0].FunctionId);
        var otherFunctionNode = nodes.First(node => node.FunctionId != nodes[0].FunctionId);
        var predicate = KernelDebugDocument.FromSource("predicate", path, source);
        var handler = KernelDebugDocument.FromSource("handler", path, source);
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = requestedPath => string.Equals(requestedPath, path, StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(source)
                : null
        });
        bridge.RegisterPackage(package with
        {
            DebugInfo = new KernelDebugInfo(
                [predicate, handler],
                [
                    new KernelSequencePoint(nodes[0].Id, new SourceSpan(1, 1, predicate.Id, 1, 12)),
                    new KernelSequencePoint(sameFunctionNode.Id, new SourceSpan(1, 4, predicate.Id, 1, 12)),
                    new KernelSequencePoint(otherFunctionNode.Id, new SourceSpan(2, 1, handler.Id, 2, 10))
                ])
        });
        const string token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        await bridge.PublishAsync(PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                "session",
                "bootstrap",
                token,
                JsonSerializer.SerializeToElement(new { sessionToken = token })),
            1024 * 1024));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);

        var response = await client.SendAsync(
            "resolve",
            new Dictionary<string, object?>
            {
                ["pluginId"] = package.Manifest.PluginId,
                ["path"] = path,
                ["lines"] = new[] { 1, 2 }
            },
            CancellationToken.None);
        var breakpoints = response.GetProperty("body").GetProperty("Breakpoints").EnumerateArray().ToArray();

        Assert.All(breakpoints, breakpoint => Assert.True(breakpoint.GetProperty("Verified").GetBoolean()));
        Assert.Equal(nodes[0].Id.Value, breakpoints[0].GetProperty("NodeId").GetString());
        Assert.Single(breakpoints[0].GetProperty("Bindings").EnumerateArray());
        Assert.Equal(otherFunctionNode.Id.Value, breakpoints[1].GetProperty("NodeId").GetString());
    }
}
