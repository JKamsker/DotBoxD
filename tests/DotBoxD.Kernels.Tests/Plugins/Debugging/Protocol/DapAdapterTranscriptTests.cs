using System.Text;
using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class DapAdapterTranscriptTests
{
    [Fact]
    public async Task Error_response_uses_the_numeric_id_required_by_dap_clients()
    {
        await using var input = new MemoryStream(Transcript(Request(1, "notSupported", new { })));
        await using var output = new MemoryStream();
        await using (var session = new DapSession(new DapConnection(input, output)))
        {
            await session.RunAsync(CancellationToken.None);
        }

        output.Position = 0;
        var response = Assert.Single(await ReadDapMessagesAsync(output));
        var error = response.GetProperty("body").GetProperty("error");
        Assert.Equal(JsonValueKind.Number, error.GetProperty("id").ValueKind);
        Assert.Contains("unsupportedCommand", error.GetProperty("format").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Event_handler_can_issue_a_bridge_request_without_blocking_the_read_loop()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceiver = async envelope =>
        {
            Assert.Equal("output", envelope.Kind);
            _ = await client.RemoteAsync(PluginDebugCommands.Threads, null, CancellationToken.None);
            received.TrySetResult();
        };
        var output = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            "output",
            Guid.NewGuid().ToString("N"),
            control.SessionToken,
            JsonSerializer.SerializeToElement(new { category = "console", output = "test" }));

        await bridge.PublishAsync(PluginDebugProtocol.Encode(output, 1024 * 1024));

        await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Bridge_rejects_wrong_discovery_token_without_losing_listener()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            new string('0', bridge.Descriptor.DiscoveryToken.Length),
            CancellationToken.None));
        await using var authenticated = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);

        Assert.Equal(control.SessionToken, authenticated.SessionToken);
    }

    [Fact]
    public async Task Bridge_leaves_breakpoint_unverified_when_local_source_checksum_is_stale()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = _ => Encoding.UTF8.GetBytes("changed")
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        var package = FireDamagePluginPackage.Create();
        var node = SandboxNodeMap.Create(package.Module).Nodes[0];
        var document = KernelDebugDocument.FromSource("document", "/source/Plugin.cs", "original");
        bridge.RegisterPackage(package with
        {
            DebugInfo = new KernelDebugInfo(
                [document],
                [new KernelSequencePoint(node.Id, new SourceSpan(1, 1, document.Id, 1, 2))])
        });
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);

        var response = await client.SendAsync(
            "resolve",
            new Dictionary<string, object?>
            {
                ["pluginId"] = package.Manifest.PluginId,
                ["path"] = document.Path,
                ["lines"] = new[] { 1 }
            },
            CancellationToken.None);

        var breakpoint = Assert.Single(
            response.GetProperty("body").GetProperty("Breakpoints").EnumerateArray().ToArray());
        Assert.False(breakpoint.GetProperty("Verified").GetBoolean());
        Assert.Contains("checksum", breakpoint.GetProperty("Message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Abrupt_adapter_disconnect_detaches_the_remote_debug_session()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        _ = await client.RemoteAsync(PluginDebugCommands.Attach, new { }, CancellationToken.None);

        await client.DisposeAsync();

        await control.DisconnectReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            new[] { PluginDebugCommands.Attach, PluginDebugCommands.Disconnect },
            control.Commands);
    }

    [Fact]
    public async Task Standard_dap_lifecycle_authenticates_bridge_resolves_breakpoint_and_configures_session()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        var package = FireDamagePluginPackage.Create() with { DebugInfo = null };
        bridge.RegisterPackage(package);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        var descriptor = bridge.Descriptor;
        var virtualPath = $"dotboxd-ir://{package.Manifest.PluginId}/module.ir";
        await using var input = new MemoryStream(Transcript(
            Request(1, "initialize", new { adapterID = "dotboxd-test", linesStartAt1 = true, columnsStartAt1 = true }),
            Request(2, "attach", new
            {
                processId = descriptor.ProcessId
            }),
            Request(3, "setBreakpoints", new
            {
                source = new { path = virtualPath },
                breakpoints = new[] { new { line = 1 } }
            }),
            Request(4, "configurationDone", new { }),
            Request(5, "disconnect", new { })));
        await using var output = new MemoryStream();
        var connection = new DapConnection(input, output);
        await using (var session = new DapSession(connection))
        {
            await session.RunAsync(CancellationToken.None);
        }

        output.Position = 0;
        var messages = await ReadDapMessagesAsync(output);
        var expectedCommands = new[]
        {
            PluginDebugCommands.Initialize,
            PluginDebugCommands.Attach,
            PluginDebugCommands.SetBreakpoints,
            PluginDebugCommands.Disconnect
        };
        Assert.True(
            expectedCommands.SequenceEqual(control.Commands),
            JsonSerializer.Serialize(messages));
        var setBreakpoints = Assert.Single(
            control.Payloads,
            item => item.Command == PluginDebugCommands.SetBreakpoints);
        var remoteBreakpoints = setBreakpoints.Payload.GetProperty("breakpoints").EnumerateArray().ToArray();
        Assert.True(remoteBreakpoints.Length == 1, JsonSerializer.Serialize(messages));
        var breakpoint = remoteBreakpoints[0];
        Assert.StartsWith("v1:", breakpoint.GetProperty("nodeId").GetString(), StringComparison.Ordinal);
        Assert.False(breakpoint.TryGetProperty("condition", out _));
        Assert.False(breakpoint.TryGetProperty("hitCount", out _));
        Assert.False(breakpoint.TryGetProperty("logMessage", out _));

        Assert.Contains(messages, item => item.GetProperty("type").GetString() == "event" && item.GetProperty("event").GetString() == "initialized");
        Assert.Contains(messages, item => item.GetProperty("type").GetString() == "event" && item.GetProperty("event").GetString() == "terminated");
        var initialize = Assert.Single(messages, item =>
            item.GetProperty("type").GetString() == "response" &&
            item.GetProperty("command").GetString() == "initialize");
        Assert.True(initialize.GetProperty("body").GetProperty("supportsCompletionsRequest").GetBoolean());
        Assert.Equal(5, messages.Count(item => item.GetProperty("type").GetString() == "response" && item.GetProperty("success").GetBoolean()));
    }

    [Fact]
    public async Task Source_resolution_returns_all_plugin_bindings_for_a_shared_sequence_point()
    {
        const string source = "return damage;";
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = _ => Encoding.UTF8.GetBytes(source)
        });
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        var package = FireDamagePluginPackage.Create();
        var document = KernelDebugDocument.FromSource("shared", "/source/Shared.cs", source);
        var point = new KernelSequencePoint(
            SandboxNodeMap.Create(package.Module).Nodes[0].Id,
            new SourceSpan(1, 1, document.Id, 1, 2));
        var debugInfo = new KernelDebugInfo([document], [point]);
        bridge.RegisterPackage(package with { Manifest = package.Manifest with { PluginId = "shared-a" }, DebugInfo = debugInfo });
        bridge.RegisterPackage(package with { Manifest = package.Manifest with { PluginId = "shared-b" }, DebugInfo = debugInfo });
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectByProcessIdAsync(
            bridge.Descriptor.ProcessId,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        var response = await client.SendAsync(
            "resolve",
            new Dictionary<string, object?> { ["pluginId"] = string.Empty, ["path"] = document.Path, ["lines"] = new[] { 1 } },
            CancellationToken.None);

        var breakpoint = Assert.Single(response.GetProperty("body").GetProperty("Breakpoints").EnumerateArray());
        Assert.Equal(2, breakpoint.GetProperty("Bindings").GetArrayLength());
    }

    private static byte[] Bootstrap(string token)
    {
        var envelope = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            "session",
            "bootstrap",
            token,
            JsonSerializer.SerializeToElement(new { sessionToken = token }));
        return PluginDebugProtocol.Encode(envelope, 1024 * 1024);
    }

    private static byte[] Transcript(params byte[][] messages) => messages.SelectMany(item => item).ToArray();

    private static byte[] Request(int sequence, string command, object arguments)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = sequence,
            type = "request",
            command,
            arguments
        });
        return Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n").Concat(payload).ToArray();
    }

    private static async Task<JsonElement[]> ReadDapMessagesAsync(Stream stream)
    {
        var connection = new DapConnection(stream, Stream.Null);
        var messages = new List<JsonElement>();
        while (await connection.ReadAsync(CancellationToken.None) is { } document)
        {
            using (document)
            {
                messages.Add(document.RootElement.Clone());
            }
        }

        return messages.ToArray();
    }

}
