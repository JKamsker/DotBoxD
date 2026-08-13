using System.Text.Json;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.DebugAdapter;

internal static class BridgeClientDiscovery
{
    public static async Task<BridgeClient> ConnectByProcessIdAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotBoxD",
            "Debug",
            processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        while (true)
        {
            try
            {
                var descriptor = JsonSerializer.Deserialize<PluginDebugBridgeDescriptor>(
                    await File.ReadAllBytesAsync(path, linked.Token).ConfigureAwait(false));
                if (descriptor?.ProcessId == processId)
                {
                    return await BridgeClient.ConnectAsync(
                            descriptor.PipeName,
                            descriptor.DiscoveryToken,
                            linked.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // The plugin may still be starting or atomically replacing its descriptor.
            }

            try
            {
                await Task.Delay(50, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No DotBoxD debug bridge appeared for plugin process {processId}.");
            }
        }
    }
}
