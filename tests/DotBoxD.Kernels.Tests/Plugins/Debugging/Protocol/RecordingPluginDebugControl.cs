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

    public Func<string, JsonElement, object>? ResponseBody { get; init; }

    public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        var request = PluginDebugProtocol.Decode(message, 1024 * 1024);
        Commands.Add(request.Kind);
        Payloads.Add((request.Kind, request.Payload.Clone()));
        if (request.Kind == PluginDebugCommands.Disconnect)
        {
            DisconnectReceived.TrySetResult();
        }

        var body = ResponseBody?.Invoke(request.Kind, request.Payload) ?? DefaultBody(request.Kind);
        var response = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            request.Kind + "Response",
            request.Id,
            SessionToken,
            JsonSerializer.SerializeToElement(new { success = true, body }));
        return ValueTask.FromResult(PluginDebugProtocol.Encode(response, 1024 * 1024));
    }

    private static object DefaultBody(string command)
        => command == PluginDebugCommands.Initialize
            ? new { supported = true }
            : command == PluginDebugCommands.SetBreakpoints
                ? new { breakpoints = Array.Empty<object>() }
                : new { };
}
