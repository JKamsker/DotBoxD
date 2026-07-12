using System.Text.Json;

namespace DotBoxD.DebugAdapter;

internal static class DapInspectionJson
{
    public static JsonElement Arguments(JsonElement request) => request.GetProperty("arguments");

    public static string DapStopReason(string? reason) => reason switch
    {
        "step" => "step",
        "pause" => "pause",
        "exception" or "breakpointConditionError" => "exception",
        _ => "breakpoint"
    };

    public static JsonElement Property(JsonElement value, string first, string second)
        => value.TryGetProperty(first, out var property) ? property : value.GetProperty(second);

    public static int? OptionalInt(JsonElement value, string first, string second)
    {
        if (!value.TryGetProperty(first, out var property) && !value.TryGetProperty(second, out property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number ? property.GetInt32() : null;
    }

    public static bool IsDebugConsole(JsonElement arguments)
        => arguments.TryGetProperty("context", out var context) &&
           context.ValueKind == JsonValueKind.String &&
           string.Equals(context.GetString(), "repl", StringComparison.Ordinal);

    public static string CompletionPrefix(string text, int column)
    {
        var end = Math.Clamp(column - 1, 0, text.Length);
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '.'))
        {
            start--;
        }

        return text[start..end];
    }

    public static void EnsureBridgeSuccess(JsonElement response)
    {
        if (!response.GetProperty("success").GetBoolean())
        {
            throw new DebugAdapterException(
                "bridgeError",
                "The plugin bridge could not serve the requested source.");
        }
    }
}
