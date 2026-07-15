using System.Text.Json;
using System.Threading.Channels;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging;

public sealed class PluginConnectionDebugProvisioningTests
{
    [Fact]
    public async Task Connection_host_provisions_duplex_debug_services_and_bootstraps_session_token()
    {
        using var server = PluginServer.Create(remoteDebugOptions: new PluginRemoteDebugOptions
        {
            Enabled = true
        });
        var pipeName = "dotboxd-debug-provisioning-" + Guid.NewGuid().ToString("N");
        await using var host = await PluginConnectionHost<object>.StartAsync(
            server,
            pipeName,
            static (_, _) => new object(),
            debugOptions: new PluginConnectionDebugOptions());
        var events = new RecordingDebugEvents();
        await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(
            pipeName,
            peer => peer.ProvidePluginDebugEvents(events));
        _ = await host.Connected.WaitAsync(TimeSpan.FromSeconds(5));
        var bootstrap = PluginDebugProtocol.Decode(
            await events.NextAsync(),
            new PluginRemoteDebugOptions().MaxMessageBytes);
        var token = bootstrap.Payload.GetProperty("sessionToken").GetString()!;
        Assert.Equal("session", bootstrap.Kind);
        Assert.Equal(token, bootstrap.SessionToken);
        var control = connection.Peer.GetPluginDebugControl();
        var request = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            PluginDebugCommands.Initialize,
            "initialize-1",
            token,
            JsonSerializer.SerializeToElement(new { }));

        var response = PluginDebugProtocol.Decode(
            await control.ExchangeAsync(PluginDebugProtocol.Encode(request, 1024 * 1024)),
            1024 * 1024);

        Assert.True(response.Payload.GetProperty("success").GetBoolean());
        Assert.True(response.Payload.GetProperty("body").GetProperty("supported").GetBoolean());
    }

    private sealed class RecordingDebugEvents : IPluginDebugEventRpcService
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();

        public ValueTask PublishAsync(byte[] message, CancellationToken cancellationToken = default)
            => _messages.Writer.WriteAsync(message, cancellationToken);

        public async Task<byte[]> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _messages.Reader.ReadAsync(timeout.Token);
        }
    }
}
