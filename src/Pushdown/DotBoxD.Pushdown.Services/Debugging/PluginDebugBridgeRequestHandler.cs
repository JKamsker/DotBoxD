using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Pushdown.Services;

internal sealed class PluginDebugBridgeRequestHandler(
    Func<byte[], CancellationToken, ValueTask<byte[]>> exchange,
    PluginDebugSourceCatalog sources,
    Action configured,
    Action<long> sourcesRefreshed)
{
    public async ValueTask<object> HandleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var id = ReadId(request);
        try
        {
            return await DispatchAsync(id, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or PluginDebugProtocolException)
        {
            return new { id, success = false, error = exception.Message };
        }
    }

    private ValueTask<object> DispatchAsync(
        string id,
        JsonElement request,
        CancellationToken cancellationToken)
        => RequiredString(request, "kind") switch
        {
            "exchange" => ExchangeAsync(id, request, cancellationToken),
            "resolve" => ValueTask.FromResult(Resolve(id, request)),
            "source" => ValueTask.FromResult(Source(id, request)),
            "location" => ValueTask.FromResult(Location(id, request)),
            "configurationDone" => ValueTask.FromResult(ConfigurationDone(id)),
            "sourcesChangedDone" => ValueTask.FromResult(SourcesChangedDone(id, request)),
            _ => ValueTask.FromResult<object>(new { id, success = false, error = "Unsupported bridge request." })
        };

    private async ValueTask<object> ExchangeAsync(
        string id,
        JsonElement request,
        CancellationToken cancellationToken)
    {
        var payload = Convert.FromBase64String(RequiredString(request, "payload"));
        var response = await exchange(payload, cancellationToken).ConfigureAwait(false);
        return new { id, success = true, payload = Convert.ToBase64String(response) };
    }

    private object Resolve(string id, JsonElement request)
    {
        var lines = request.GetProperty("lines").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var resolution = sources.Resolve(
            OptionalString(request, "pluginId"),
            RequiredString(request, "path"),
            lines);
        return new { id, success = true, body = resolution };
    }

    private object Source(string id, JsonElement request)
    {
        var content = sources.Source(OptionalString(request, "pluginId"), RequiredString(request, "path"));
        return content is null
            ? new { id, success = false, content = (string?)null }
            : new { id, success = true, content = (string?)content };
    }

    private object Location(string id, JsonElement request)
    {
        var location = sources.Location(
            OptionalString(request, "pluginId"),
            RequiredString(request, "nodeId"));
        return new { id, success = location is not null, body = location };
    }

    private object ConfigurationDone(string id)
    {
        configured();
        return new { id, success = true };
    }

    private object SourcesChangedDone(string id, JsonElement request)
    {
        sourcesRefreshed(RequiredInt64(request, "version"));
        return new { id, success = true };
    }

    private static string RequiredString(JsonElement request, string name)
        => request.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new ArgumentException($"Bridge request {name} is null.")
            : throw new ArgumentException($"Bridge request {name} is required.");

    private static string ReadId(JsonElement request)
        => request.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long RequiredInt64(JsonElement request, string name)
        => request.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new ArgumentException($"Bridge request {name} must be an integer.");

    private static string OptionalString(JsonElement request, string name)
        => request.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
