using System.Text;
using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class DapBreakpointTranslationTests
{
    [Fact]
    public async Task Source_breakpoint_expressions_use_runtime_slot_names()
    {
        const string path = "/source/ConditionalBreakpoint.cs";
        const string source = "return e.Damage > 10;";
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = requested => string.Equals(requested, path, StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(source)
                : null
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        var package = FireDamagePluginPackage.Create();
        var node = SandboxNodeMap.Create(package.Module).Nodes[0];
        var document = KernelDebugDocument.FromSource("conditional", path, source);
        bridge.RegisterPackage(package with
        {
            DebugInfo = new KernelDebugInfo(
                [document],
                [new KernelSequencePoint(node.Id, new SourceSpan(1, 1, document.Id, 1, source.Length))],
                [new KernelDebugVariableBinding(node.FunctionId, "e_Damage", "e.Damage")])
        });
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        await using var output = new MemoryStream();
        var handler = new DapBreakpointHandler(new DapConnection(Stream.Null, output), client, string.Empty);
        using var request = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = 1,
            type = "request",
            command = "setBreakpoints",
            arguments = new
            {
                source = new { path },
                breakpoints = new[]
                {
                    new
                    {
                        line = 1,
                        condition = "e.Damage > 10",
                        hitCondition = "2",
                        logMessage = "literal e.Damage; value={e.Damage}"
                    }
                }
            }
        }));

        await handler.HandleAsync(request.RootElement, CancellationToken.None);

        var configured = Assert.Single(
            control.Payloads,
            item => item.Command == PluginDebugCommands.SetBreakpoints);
        var breakpoint = Assert.Single(configured.Payload.GetProperty("breakpoints").EnumerateArray());
        Assert.Equal("e_Damage > 10", breakpoint.GetProperty("condition").GetString());
        Assert.Equal(2, breakpoint.GetProperty("hitCount").GetInt32());
        Assert.Equal(
            "literal e.Damage; value={e_Damage}",
            breakpoint.GetProperty("logMessage").GetString());
    }

    [Fact]
    public async Task Invalid_hit_condition_is_rejected_instead_of_becoming_unconditional()
    {
        const string path = "/source/InvalidHitCondition.cs";
        const string source = "return true;";
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = requested => string.Equals(requested, path, StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(source)
                : null
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        var package = FireDamagePluginPackage.Create();
        var node = SandboxNodeMap.Create(package.Module).Nodes[0];
        var document = KernelDebugDocument.FromSource("invalid-hit", path, source);
        bridge.RegisterPackage(package with
        {
            DebugInfo = new KernelDebugInfo(
                [document],
                [new KernelSequencePoint(node.Id, new SourceSpan(1, 1, document.Id, 1, source.Length))])
        });
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        await using var output = new MemoryStream();
        var handler = new DapBreakpointHandler(new DapConnection(Stream.Null, output), client, string.Empty);
        using var request = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = 1,
            type = "request",
            command = "setBreakpoints",
            arguments = new
            {
                source = new { path },
                breakpoints = new[] { new { line = 1, hitCondition = "every time" } }
            }
        }));

        var exception = await Assert.ThrowsAsync<DebugAdapterException>(
            async () => await handler.HandleAsync(request.RootElement, CancellationToken.None));

        Assert.Equal("invalidBreakpoint", exception.Code);
        Assert.Empty(control.Payloads);
    }

    private static byte[] Bootstrap(string token)
        => PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                "session",
                "bootstrap",
                token,
                JsonSerializer.SerializeToElement(new { sessionToken = token })),
            1024 * 1024);
}
