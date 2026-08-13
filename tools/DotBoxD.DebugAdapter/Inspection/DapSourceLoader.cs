using System.Text.Json;
using static DotBoxD.DebugAdapter.DapInspectionJson;

namespace DotBoxD.DebugAdapter;

internal static class DapSourceLoader
{
    public static async ValueTask<object> LoadAsync(
        BridgeClient bridge,
        string pluginId,
        JsonElement request,
        CancellationToken cancellationToken)
    {
        var path = Arguments(request).GetProperty("source").GetProperty("path").GetString()!;
        var response = await bridge.SendAsync(
                "source",
                new Dictionary<string, object?> { ["pluginId"] = pluginId, ["path"] = path },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureBridgeSuccess(response);
        return new { content = response.GetProperty("content").GetString(), mimeType = "text/plain" };
    }
}
