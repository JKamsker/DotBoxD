using System.Text;
using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class DapLateSourceRegistrationTests
{
    [Fact]
    public async Task Breakpoints_are_rebound_when_a_source_package_is_registered_after_attach()
    {
        const string source = "return damage;";
        const string path = "/source/LateChain.cs";
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = _ => Encoding.UTF8.GetBytes(source)
        });
        var control = new BreakpointRecordingControl();
        bridge.AttachControl(control);
        var package = FireDamagePluginPackage.Create() with { DebugInfo = null };
        bridge.RegisterPackage(package);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        await using var output = new MemoryStream();
        var handler = new DapBreakpointHandler(new DapConnection(Stream.Null, output), client, string.Empty);
        client.SourcesChangedReceiver = handler.OnSourcesChangedAsync;
        using var request = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = 1,
            type = "request",
            command = "setBreakpoints",
            arguments = new { source = new { path }, breakpoints = new[] { new { line = 1 } } }
        }));

        await handler.HandleAsync(request.RootElement, CancellationToken.None);
        var node = SandboxNodeMap.Create(package.Module).Nodes[0];
        var document = KernelDebugDocument.FromSource("late-chain", path, source);
        bridge.RegisterPackage(package with
        {
            DebugInfo = new KernelDebugInfo(
                [document],
                [new KernelSequencePoint(node.Id, new SourceSpan(1, 1, document.Id, 1, 2))])
        });

        var payload = await control.BreakpointConfigured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(payload.GetProperty("breakpoints").EnumerateArray());
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

    private sealed class BreakpointRecordingControl : IPluginDebugControlRpcService
    {
        public string SessionToken { get; } = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) +
            Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

        public TaskCompletionSource<JsonElement> BreakpointConfigured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
        {
            var request = PluginDebugProtocol.Decode(message, 1024 * 1024);
            if (request.Kind == PluginDebugCommands.SetBreakpoints &&
                request.Payload.GetProperty("breakpoints").GetArrayLength() > 0)
            {
                BreakpointConfigured.TrySetResult(request.Payload.Clone());
            }

            object body = request.Kind == PluginDebugCommands.Initialize
                ? new { supported = true }
                : request.Kind == PluginDebugCommands.SetBreakpoints
                    ? new { breakpoints = Array.Empty<object>() }
                    : new { };
            var response = new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                request.Kind + "Response",
                request.Id,
                SessionToken,
                JsonSerializer.SerializeToElement(new { success = true, body }));
            return ValueTask.FromResult(PluginDebugProtocol.Encode(response, 1024 * 1024));
        }
    }
}
