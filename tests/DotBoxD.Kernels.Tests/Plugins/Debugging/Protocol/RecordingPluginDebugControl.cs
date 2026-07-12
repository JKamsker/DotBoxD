using System.Text.Json;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

internal sealed class RecordingPluginDebugControl : IPluginDebugControlRpcService
{
    public string SessionToken { get; } = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) +
        Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

    public List<string> Commands { get; } = [];

    public List<(string Command, JsonElement Payload)> Payloads { get; } = [];

    public TaskCompletionSource DisconnectReceived { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        var request = PluginDebugProtocol.Decode(message, 1024 * 1024);
        Commands.Add(request.Kind);
        Payloads.Add((request.Kind, request.Payload.Clone()));
        if (request.Kind == PluginDebugCommands.Disconnect)
        {
            DisconnectReceived.TrySetResult();
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
