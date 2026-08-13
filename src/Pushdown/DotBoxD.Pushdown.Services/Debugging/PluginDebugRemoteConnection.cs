using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Pushdown.Services;

internal sealed class PluginDebugRemoteConnection(int maxMessageBytes)
{
    private readonly int _maxMessageBytes = maxMessageBytes;
    private readonly TaskCompletionSource _controlAttached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IPluginDebugControlRpcService? _control;
    private int _attached;

    public Task ControlAttached => _controlAttached.Task;

    public void Attach(IPluginDebugControlRpcService control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (Interlocked.CompareExchange(ref _control, control, null) is not null)
        {
            throw new InvalidOperationException("A remote debug control endpoint is already attached.");
        }

        _controlAttached.TrySetResult();
    }

    public async ValueTask<byte[]> ExchangeAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var control = Volatile.Read(ref _control)
            ?? throw new InvalidOperationException("The remote debug control endpoint is not connected.");
        var command = PluginDebugProtocol.Decode(payload, _maxMessageBytes).Kind;
        var response = await control.ExchangeAsync(payload, cancellationToken).ConfigureAwait(false);
        if (Succeeded(response, _maxMessageBytes))
        {
            if (command == PluginDebugCommands.Attach)
            {
                Volatile.Write(ref _attached, 1);
            }
            else if (command == PluginDebugCommands.Disconnect)
            {
                Volatile.Write(ref _attached, 0);
            }
        }

        return response;
    }

    public async ValueTask DisconnectAsync(string sessionToken)
    {
        if (Interlocked.Exchange(ref _attached, 0) == 0 || Volatile.Read(ref _control) is not { } control)
        {
            return;
        }

        var request = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            PluginDebugCommands.Disconnect,
            Guid.NewGuid().ToString("N"),
            sessionToken,
            JsonSerializer.SerializeToElement(new { }));
        try
        {
            await control.ExchangeAsync(
                    PluginDebugProtocol.Encode(request, _maxMessageBytes),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The bridge is already lost; best-effort detach cannot recover a failed remote transport.
        }
    }

    private static bool Succeeded(byte[] response, int maxMessageBytes)
    {
        try
        {
            var payload = PluginDebugProtocol.Decode(response, maxMessageBytes).Payload;
            return payload.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
        }
        catch (PluginDebugProtocolException)
        {
            return false;
        }
    }
}
