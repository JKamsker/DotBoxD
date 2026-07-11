using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class DapInspectionHandlerTests
{
    [Fact]
    public async Task Resuming_one_execution_preserves_a_concurrent_stop()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        const string sessionToken = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        await bridge.PublishAsync(Envelope("session", "bootstrap", sessionToken, new { sessionToken }));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        await using var output = new MemoryStream();
        var handler = new DapInspectionHandler(new DapConnection(Stream.Null, output), client, string.Empty);

        await handler.OnRemoteEventAsync(Stopped(sessionToken, "first-run", "first-plugin"));
        await handler.OnRemoteEventAsync(Stopped(sessionToken, "second-run", "second-plugin"));
        handler.InvalidateStoppedState(1);

        Assert.Throws<DebugAdapterException>(() => handler.RunId(1));
        Assert.Equal("second-run", handler.RunId(2));
        output.Position = 0;
        var reader = new DapConnection(output, Stream.Null);
        using var first = await reader.ReadAsync(CancellationToken.None);
        using var second = await reader.ReadAsync(CancellationToken.None);
        Assert.False(first!.RootElement.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean());
        Assert.False(second!.RootElement.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean());
    }

    private static PluginDebugEnvelope Stopped(string token, string runId, string pluginId)
        => new(
            PluginDebugProtocol.Version,
            "stopped",
            Guid.NewGuid().ToString("N"),
            token,
            JsonSerializer.SerializeToElement(new
            {
                runId,
                pluginId,
                nodeId = "v1:test",
                reason = "breakpoint"
            }));

    private static byte[] Envelope(string kind, string id, string token, object payload)
        => PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                kind,
                id,
                token,
                JsonSerializer.SerializeToElement(payload)),
            1024 * 1024);
}
