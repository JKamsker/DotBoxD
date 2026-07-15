using System.Text.Json;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class PluginDebugRequestValidationTests
{
    [Fact]
    public async Task Execution_commands_fail_closed_without_an_attachment_or_stopped_run()
    {
        using var server = DebugServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(NullEvents.Instance);

        await AssertErrorAsync(debug, PluginDebugCommands.Pause, new { }, "notAttached");
        foreach (var command in new[]
                 {
                     PluginDebugCommands.Continue,
                     PluginDebugCommands.StepIn,
                     PluginDebugCommands.StepOver,
                     PluginDebugCommands.StepOut
                 })
        {
            await AssertErrorAsync(debug, command, new { runId = "stale" }, "staleRun");
        }

        await AssertErrorAsync(debug, PluginDebugCommands.StackTrace, new { runId = "stale" }, "staleRun");
        await AssertErrorAsync(debug, PluginDebugCommands.Variables, new { frameId = "stale:0" }, "staleFrame");
        await AssertErrorAsync(
            debug,
            PluginDebugCommands.SetVariable,
            new { frameId = "stale:0", name = "value", value = new { value = 1 } },
            "staleFrame");
        await AssertErrorAsync(debug, "futureCommand", new { }, "unsupportedCommand");
    }

    [Fact]
    public async Task Breakpoint_validation_rejects_every_malformed_optional_field()
    {
        using var server = DebugServer();
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(NullEvents.Instance);
        await AssertErrorAsync(debug, PluginDebugCommands.SetBreakpoints, new { }, "invalidArguments");
        string[] invalidPayloads =
        [
            "{\"pluginId\":\"plugin\"}",
            "{\"pluginId\":\"plugin\",\"nodeIds\":{}}",
            "{\"pluginId\":\"plugin\",\"nodeIds\":[42]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":{}}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"condition\":42}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"condition\":\"\"}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"condition\":\"too-long-condition\"}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"logMessage\":42}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"logMessage\":\"\"}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"logMessage\":\"too-long-logpoint\"}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"hitCount\":\"two\"}]}",
            "{\"pluginId\":\"plugin\",\"breakpoints\":[{\"nodeId\":\"v1:function:s0\",\"hitCount\":0}]}"
        ];

        foreach (var json in invalidPayloads)
        {
            var response = await ExchangeAsync(
                debug,
                PluginDebugCommands.SetBreakpoints,
                JsonSerializer.Deserialize<JsonElement>(json));
            Assert.False(response.GetProperty("success").GetBoolean(), json);
            Assert.Equal("invalidBreakpoint", response.GetProperty("error").GetProperty("code").GetString());
        }

        var package = FireDamagePluginPackage.Create();
        var nodeId = SandboxNodeMap.Create(package.Module).Nodes[0].Id.Value;
        var accepted = await ExchangeAsync(
            debug,
            PluginDebugCommands.SetBreakpoints,
            JsonSerializer.SerializeToElement(new
            {
                pluginId = package.Manifest.PluginId,
                nodeIds = new[] { nodeId, nodeId }
            }));
        Assert.True(accepted.GetProperty("success").GetBoolean());
        Assert.Single(accepted.GetProperty("body").GetProperty("breakpoints").EnumerateArray());
    }

    [Fact]
    public async Task Snapshot_limit_returns_a_bounded_protocol_error()
    {
        using var server = PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                MaxSnapshotBytes = 1
            });
        using var owner = server.CreateSession();
        await using var debug = owner.CreateDebugSession(NullEvents.Instance);

        await AssertErrorAsync(debug, PluginDebugCommands.Threads, new { }, "snapshotTooLarge");
    }

    [Fact]
    public async Task Response_envelope_overflow_returns_a_bounded_protocol_error()
    {
        const int messageLimit = 512;
        using var limitedServer = PluginServer.Create(
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                MaxSnapshotBytes = 4096,
                MaxMessageBytes = messageLimit
            },
            defaultPolicy: PluginAddendumTestPolicies.LongWall());
        using var limitedOwner = limitedServer.CreateSession();
        await using var limitedDebug = limitedOwner.CreateDebugSession(NullEvents.Instance);

        var response = await ExchangeBytesAsync(limitedDebug, PluginDebugCommands.Initialize, messageLimit);
        var payload = PluginDebugProtocol.Decode(response, messageLimit).Payload;

        Assert.False(payload.GetProperty("success").GetBoolean());
        Assert.Equal("snapshotTooLarge", payload.GetProperty("error").GetProperty("code").GetString());
    }

    private static PluginServer DebugServer()
        => PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            remoteDebugOptions: new PluginRemoteDebugOptions
            {
                Enabled = true,
                MaxExpressionLength = 8
            });

    private static async Task AssertErrorAsync(
        PluginDebugSession session,
        string command,
        object payload,
        string expectedCode)
    {
        var response = await ExchangeAsync(session, command, JsonSerializer.SerializeToElement(payload));
        Assert.False(response.GetProperty("success").GetBoolean());
        Assert.Equal(expectedCode, response.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ExchangeAsync(
        PluginDebugSession session,
        string command,
        JsonElement payload)
    {
        var request = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            command,
            Guid.NewGuid().ToString("N"),
            session.SessionToken,
            payload);
        var response = await session.ExchangeAsync(PluginDebugProtocol.Encode(request, 1024 * 1024));
        return PluginDebugProtocol.Decode(response, 1024 * 1024).Payload;
    }

    private static async Task<byte[]> ExchangeBytesAsync(
        PluginDebugSession session,
        string command,
        int maxMessageBytes)
    {
        var request = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            command,
            Guid.NewGuid().ToString("N"),
            session.SessionToken,
            JsonSerializer.SerializeToElement(new { }));
        return await session.ExchangeAsync(PluginDebugProtocol.Encode(request, maxMessageBytes));
    }

    private sealed class NullEvents : IPluginDebugEventEndpoint
    {
        public static NullEvents Instance { get; } = new();

        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
