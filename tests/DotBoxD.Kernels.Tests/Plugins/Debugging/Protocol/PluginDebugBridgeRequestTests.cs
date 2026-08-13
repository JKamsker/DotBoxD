using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class PluginDebugBridgeRequestTests
{
    [Fact]
    public void Republishing_discovery_descriptor_tightens_existing_unix_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var descriptor = new PluginDebugBridgeDescriptor(
            int.MaxValue - Environment.ProcessId,
            "permission-test-pipe",
            new string('a', 64));
        var published = PublishDescriptor(descriptor);
        try
        {
            File.SetUnixFileMode(
                published.DiscoveryFile!,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            published = PublishDescriptor(descriptor);

            var mode = File.GetUnixFileMode(published.DiscoveryFile!);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
        }
        finally
        {
            File.Delete(published.DiscoveryFile!);
        }
    }

    [Fact]
    public async Task Bridge_preserves_headroom_for_base64_wrapped_remote_envelopes()
    {
        const int payloadLength = 800_000;
        const int maxFrameBytes = 1024 * 1024;
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var control = new LargeResponseControl(payloadLength, maxFrameBytes);
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);

        var response = await client.RemoteAsync("largeResponse", null, CancellationToken.None);

        Assert.Equal(payloadLength, response.GetProperty("data").GetString()!.Length);
    }

    [Fact]
    public async Task Bridge_and_adapter_honor_a_configured_frame_limit_above_the_default()
    {
        const int payloadLength = 1_200_000;
        const int maxFrameBytes = 2 * 1024 * 1024;
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            MaxFrameBytes = maxFrameBytes
        });
        var control = new LargeResponseControl(payloadLength, maxFrameBytes);
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);

        var response = await client.RemoteAsync("largeResponse", null, CancellationToken.None);

        Assert.Equal(payloadLength, response.GetProperty("data").GetString()!.Length);
    }

    [Fact]
    public async Task Bootstrap_timeout_rejects_only_the_premature_client()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            DebuggerWaitTimeout = TimeSpan.FromMilliseconds(40)
        });

        await Assert.ThrowsAsync<EndOfStreamException>(() => BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None));

        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            timeout.Token);
        Assert.Equal(control.SessionToken, client.SessionToken);
    }

    [Fact]
    public async Task Bridge_rejects_source_refresh_acknowledgements_for_unregistered_versions()
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

        var response = await client.SendAsync(
            "sourcesChangedDone",
            Arguments(("version", 1L)),
            CancellationToken.None);

        Assert.False(response.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Bridge_serves_mapped_and_virtual_sources_and_rejects_bad_requests()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var mapped = FireDamagePluginPackage.Create();
        bridge.RegisterPackage(mapped);
        var virtualPackage = VirtualPackage(mapped);
        bridge.RegisterPackage(virtualPackage);
        var control = new RecordingPluginDebugControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        var debugInfo = Assert.IsType<KernelDebugInfo>(mapped.DebugInfo);
        var document = debugInfo.Documents[0];
        var point = debugInfo.SequencePoints.First(item => item.Span.DocumentId == document.Id);

        var resolved = await client.SendAsync(
            "resolve",
            Arguments(
                ("pluginId", mapped.Manifest.PluginId),
                ("path", document.Path),
                ("lines", new[] { point.Span.Line })),
            CancellationToken.None);
        Assert.True(resolved.GetProperty("success").GetBoolean());
        Assert.NotEmpty(resolved.GetProperty("body").GetProperty("Breakpoints").EnumerateArray());

        var source = await client.SendAsync(
            "source",
            Arguments(("pluginId", mapped.Manifest.PluginId), ("path", document.Path)),
            CancellationToken.None);
        Assert.False(source.GetProperty("success").GetBoolean());

        var location = await client.SendAsync(
            "location",
            Arguments(("pluginId", mapped.Manifest.PluginId), ("nodeId", point.NodeId.Value)),
            CancellationToken.None);
        Assert.True(location.GetProperty("success").GetBoolean());
        Assert.Equal(document.Path, location.GetProperty("body").GetProperty("Path").GetString());

        var virtualPath = $"dotboxd-ir://{virtualPackage.Manifest.PluginId}/module.ir";
        var virtualSource = await client.SendAsync(
            "source",
            Arguments(("pluginId", virtualPackage.Manifest.PluginId), ("path", virtualPath)),
            CancellationToken.None);
        Assert.True(virtualSource.GetProperty("success").GetBoolean());
        Assert.Contains("Function", virtualSource.GetProperty("content").GetString(), StringComparison.Ordinal);

        var virtualResolved = await client.SendAsync(
            "resolve",
            Arguments(
                ("pluginId", virtualPackage.Manifest.PluginId),
                ("path", virtualPath),
                ("lines", new[] { 1, int.MaxValue })),
            CancellationToken.None);
        var virtualBreakpoints = virtualResolved.GetProperty("body").GetProperty("Breakpoints").EnumerateArray().ToArray();
        Assert.True(virtualBreakpoints[0].GetProperty("Verified").GetBoolean());
        Assert.False(virtualBreakpoints[1].GetProperty("Verified").GetBoolean());
        var wildcardSource = await client.SendAsync(
            "source",
            Arguments(("pluginId", "*"), ("path", virtualPath)),
            CancellationToken.None);
        Assert.True(wildcardSource.GetProperty("success").GetBoolean());

        var configured = await client.SendAsync("configurationDone", null, CancellationToken.None);
        Assert.True(configured.GetProperty("success").GetBoolean());
        var missing = await client.SendAsync(
            "source",
            Arguments(("pluginId", mapped.Manifest.PluginId), ("path", "/missing.cs")),
            CancellationToken.None);
        Assert.False(missing.GetProperty("success").GetBoolean());
        var unknownNode = await client.SendAsync(
            "location",
            Arguments(("pluginId", mapped.Manifest.PluginId), ("nodeId", "v1:missing:s0")),
            CancellationToken.None);
        Assert.False(unknownNode.GetProperty("success").GetBoolean());
        var invalid = await client.SendAsync("resolve", Arguments(("path", document.Path)), CancellationToken.None);
        Assert.False(invalid.GetProperty("success").GetBoolean());
        var unknownPlugin = await client.SendAsync(
            "resolve",
            Arguments(("pluginId", "missing"), ("path", document.Path), ("lines", new[] { 1 })),
            CancellationToken.None);
        Assert.False(unknownPlugin.GetProperty("success").GetBoolean());
        var invalidSource = await client.SendAsync("source", null, CancellationToken.None);
        Assert.False(invalidSource.GetProperty("success").GetBoolean());
        var invalidLocation = await client.SendAsync("location", null, CancellationToken.None);
        Assert.False(invalidLocation.GetProperty("success").GetBoolean());
        var disconnectedRemote = await client.SendAsync(
            "exchange",
            Arguments(("payload", Convert.ToBase64String([1, 2, 3]))),
            CancellationToken.None);
        Assert.False(disconnectedRemote.GetProperty("success").GetBoolean());
        var unsupported = await client.SendAsync("future", null, CancellationToken.None);
        Assert.False(unsupported.GetProperty("success").GetBoolean());
    }

    private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] values)
        => values.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private static PluginDebugBridgeDescriptor PublishDescriptor(PluginDebugBridgeDescriptor descriptor)
    {
        var discovery = typeof(PluginDebugBridge).Assembly.GetType(
            "DotBoxD.Pushdown.Services.PluginDebugBridgeDiscovery",
            throwOnError: true)!;
        var publish = discovery.GetMethod(
            "Publish",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        return Assert.IsType<PluginDebugBridgeDescriptor>(publish.Invoke(null, [descriptor]));
    }

    private static byte[] Bootstrap(string sessionToken)
        => PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                "session",
                "bootstrap",
                sessionToken,
                JsonSerializer.SerializeToElement(new { })),
            1024 * 1024);

    private static PluginPackage VirtualPackage(PluginPackage template)
    {
        const string pluginId = "virtual-debug-package";
        var metadata = template.Module.Metadata.ToDictionary();
        metadata["pluginId"] = pluginId;
        return PluginPackage.Create(
            template.Manifest with { PluginId = pluginId },
            template.Module with { Id = pluginId, Metadata = metadata },
            template.Entrypoints);
    }

    private sealed class LargeResponseControl(int payloadLength, int maxMessageBytes) : IPluginDebugControlRpcService
    {
        public string SessionToken { get; } = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) +
            Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

        public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
        {
            var request = PluginDebugProtocol.Decode(message, maxMessageBytes);
            var response = new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                request.Kind + "Response",
                request.Id,
                SessionToken,
                JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    body = new { data = new string('x', payloadLength) }
                }));
            return ValueTask.FromResult(PluginDebugProtocol.Encode(response, maxMessageBytes));
        }
    }
}
