using System.Collections.Concurrent;
using System.Text.Json;
using DotBoxD.DebugAdapter.Diagnostics;
using DotBoxD.Plugins.Debugging;
using static DotBoxD.DebugAdapter.DapInspectionJson;

namespace DotBoxD.DebugAdapter;

internal sealed class DapStackTraceLoader(BridgeClient bridge)
{
    private readonly ConcurrentDictionary<int, object> _cache = [];

    public void Invalidate(int threadId) => _cache.TryRemove(threadId, out _);

    public void Clear() => _cache.Clear();

    public async ValueTask<object> LoadAsync(
        int threadId,
        string runId,
        string pluginId,
        DapStoppedExecutionStore stopped,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(threadId, out var cached))
        {
            return cached;
        }

        var body = await bridge.RemoteAsync(
                PluginDebugCommands.StackTrace,
                new { runId },
                cancellationToken)
            .ConfigureAwait(false);
        var frames = new List<object>();
        foreach (var frame in body.GetProperty("frames").EnumerateArray())
        {
            var location = await LocationAsync(frame, pluginId, cancellationToken).ConfigureAwait(false);
            var frameId = stopped.AddFrame(
                threadId,
                frame.GetProperty("frameId").GetString()!,
                pluginId,
                frame.GetProperty("functionId").GetString()!,
                location.Bindings);
            AdapterDiagnostics.Write(
                $"stack {threadId} {frame.GetProperty("functionId").GetString()} line {location.Line}");
            frames.Add(new
            {
                id = frameId,
                name = frame.GetProperty("functionId").GetString(),
                source = location.Source,
                line = location.Line,
                column = location.Column,
                endLine = location.EndLine,
                endColumn = location.EndColumn
            });
        }

        var result = new { stackFrames = frames, totalFrames = frames.Count };
        _cache[threadId] = result;
        return result;
    }

    private async ValueTask<(
        object? Source,
        int Line,
        int Column,
        int? EndLine,
        int? EndColumn,
        IReadOnlyList<DapSourceVariableBinding> Bindings)> LocationAsync(
        JsonElement frame,
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (!frame.TryGetProperty("nodeId", out var node) || node.ValueKind != JsonValueKind.String)
        {
            return (null, 1, 1, null, null, []);
        }

        var response = await bridge.SendAsync(
                "location",
                new Dictionary<string, object?> { ["pluginId"] = pluginId, ["nodeId"] = node.GetString() },
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.GetProperty("success").GetBoolean())
        {
            return (null, 1, 1, null, null, []);
        }

        var location = response.GetProperty("body");
        var path = Property(location, "Path", "path").GetString()!;
        var source = new
        {
            name = Path.GetFileName(path),
            path,
            sourceReference = path.StartsWith("dotboxd-ir://", StringComparison.Ordinal) ? 1 : 0
        };
        return (
            source,
            Property(location, "Line", "line").GetInt32(),
            Property(location, "Column", "column").GetInt32(),
            OptionalInt(location, "EndLine", "endLine"),
            OptionalInt(location, "EndColumn", "endColumn"),
            Property(location, "VariableBindings", "variableBindings").EnumerateArray()
                .Select(binding => new DapSourceVariableBinding(
                    Property(binding, "SlotName", "slotName").GetString()!,
                    Property(binding, "SourceName", "sourceName").GetString()!,
                    OptionalString(binding, "TypeName", "typeName"),
                    OptionalString(binding, "DisplayValue", "displayValue")))
                .ToArray());
    }

    private static string? OptionalString(JsonElement value, string first, string second)
    {
        var property = value.TryGetProperty(first, out var found) ? found : value.GetProperty(second);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
}
