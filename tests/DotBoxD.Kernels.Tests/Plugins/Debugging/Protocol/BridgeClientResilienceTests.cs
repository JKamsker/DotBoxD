using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class BridgeClientResilienceTests
{
    [Fact]
    public async Task Authentication_waits_until_remote_control_is_attached()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            DebuggerWaitTimeout = TimeSpan.FromMilliseconds(200)
        });
        var control = new DelayedControl();
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        using var premature = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            premature.Token));

        await Task.Delay(250);
        bridge.AttachControl(control);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            timeout.Token);
        Assert.Equal(control.SessionToken, client.SessionToken);
    }

    [Fact]
    public async Task Late_response_after_local_timeout_does_not_disconnect_adapter()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        var control = new DelayedControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None,
            TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<DebugAdapterException>(
            () => client.RemoteAsync("slow", null, CancellationToken.None).AsTask());
        Assert.Equal("bridgeTimeout", exception.Code);
        await control.SlowResponseCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        var response = await client.RemoteAsync("fast", null, CancellationToken.None);
        Assert.Equal("fast", response.GetProperty("command").GetString());
    }

    [Fact]
    public async Task Reverse_events_use_negotiated_message_limit()
    {
        const int maxMessageBytes = 2 * 1024 * 1024;
        const int outputLength = 1_200_000;
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            MaxFrameBytes = maxMessageBytes
        });
        var control = new DelayedControl();
        bridge.AttachControl(control);
        await bridge.PublishAsync(Bootstrap(control.SessionToken));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        var received = new TaskCompletionSource<PluginDebugEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceiver = envelope =>
        {
            if (envelope.Kind == "output")
            {
                received.TrySetResult(envelope);
            }

            return ValueTask.CompletedTask;
        };
        var output = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            "output",
            Guid.NewGuid().ToString("N"),
            control.SessionToken,
            JsonSerializer.SerializeToElement(new { output = new string('x', outputLength) }));

        await bridge.PublishAsync(PluginDebugProtocol.Encode(output, maxMessageBytes));

        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(outputLength, envelope.Payload.GetProperty("output").GetString()!.Length);
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

    private sealed class DelayedControl : IPluginDebugControlRpcService
    {
        public string SessionToken { get; } = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) +
            Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

        public TaskCompletionSource SlowResponseCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<byte[]> ExchangeAsync(
            byte[] message,
            CancellationToken cancellationToken = default)
        {
            var request = PluginDebugProtocol.Decode(message, 2 * 1024 * 1024);
            if (request.Kind == "slow")
            {
                await Task.Delay(150, cancellationToken);
            }

            var response = PluginDebugProtocol.Encode(
                new PluginDebugEnvelope(
                    PluginDebugProtocol.Version,
                    request.Kind + "Response",
                    request.Id,
                    SessionToken,
                    JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        body = new { command = request.Kind }
                    })),
                2 * 1024 * 1024);
            if (request.Kind == "slow")
            {
                SlowResponseCompleted.TrySetResult();
            }

            return response;
        }
    }
}
