using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class DapInspectionHandler(
    DapConnection connection,
    BridgeClient bridge,
    string pluginId)
{
    private readonly Dictionary<int, string> _threads = [];
    private readonly Dictionary<string, int> _threadIds = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _frames = [];
    private readonly DapVariableStore _variableStore = new();
    private int _nextThreadId;
    private int _nextFrameId;

    public bool Handles(string command) => command is
        "threads" or "stackTrace" or "scopes" or "variables" or "evaluate" or
        "setVariable" or "setExpression" or "source";

    public async ValueTask HandleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var command = request.GetProperty("command").GetString()!;
        object body = command switch
        {
            "threads" => await ThreadsAsync(cancellationToken).ConfigureAwait(false),
            "stackTrace" => await StackTraceAsync(request, cancellationToken).ConfigureAwait(false),
            "scopes" => Scopes(request),
            "variables" => await VariablesAsync(request, cancellationToken).ConfigureAwait(false),
            "evaluate" => await EvaluateAsync(request, cancellationToken).ConfigureAwait(false),
            "setVariable" => await SetVariableAsync(request, cancellationToken).ConfigureAwait(false),
            "setExpression" => await SetExpressionAsync(request, cancellationToken).ConfigureAwait(false),
            "source" => await SourceAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new DebugAdapterException("unsupported", $"Unsupported inspection command '{command}'.")
        };
        await connection.RespondAsync(request, success: true, body, message: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask OnRemoteEventAsync(PluginDebugEnvelope envelope)
    {
        if (envelope.Kind == "stopped")
        {
            var runId = envelope.Payload.GetProperty("runId").GetString()!;
            var threadId = ThreadId(runId);
            var reason = envelope.Payload.GetProperty("reason").GetString();
            await connection.EventAsync(
                    "stopped",
                    new { reason = DapStopReason(reason), threadId, allThreadsStopped = true })
                .ConfigureAwait(false);
        }
        else if (envelope.Kind == "output")
        {
            await connection.EventAsync(
                    "output",
                    new
                    {
                        category = envelope.Payload.GetProperty("category").GetString(),
                        output = envelope.Payload.GetProperty("output").GetString() + Environment.NewLine
                    })
                .ConfigureAwait(false);
        }
    }

    public string RunId(int threadId)
        => _threads.TryGetValue(threadId, out var runId)
            ? runId
            : throw new DebugAdapterException("staleThread", "The selected kernel execution is no longer stopped.");

    private async ValueTask<object> ThreadsAsync(CancellationToken cancellationToken)
    {
        var body = await bridge.RemoteAsync(PluginDebugCommands.Threads, null, cancellationToken).ConfigureAwait(false);
        var threads = body.GetProperty("threads").EnumerateArray()
            .Select(thread => new
            {
                id = ThreadId(thread.GetProperty("runId").GetString()!),
                name = thread.GetProperty("name").GetString()
            })
            .ToArray();
        return new { threads };
    }

    private async ValueTask<object> StackTraceAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var threadId = Arguments(request).GetProperty("threadId").GetInt32();
        var runId = RunId(threadId);
        var body = await bridge.RemoteAsync(
                PluginDebugCommands.StackTrace,
                new { runId },
                cancellationToken)
            .ConfigureAwait(false);
        var frames = new List<object>();
        foreach (var frame in body.GetProperty("frames").EnumerateArray())
        {
            var frameId = Interlocked.Increment(ref _nextFrameId);
            _frames[frameId] = frame.GetProperty("frameId").GetString()!;
            var location = await LocationAsync(frame, cancellationToken).ConfigureAwait(false);
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

        return new { stackFrames = frames, totalFrames = frames.Count };
    }

    private object Scopes(JsonElement request)
    {
        var frameId = Arguments(request).GetProperty("frameId").GetInt32();
        var remoteFrame = Frame(frameId);
        return _variableStore.Scopes(remoteFrame);
    }

    private async ValueTask<object> VariablesAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var reference = Arguments(request).GetProperty("variablesReference").GetInt32();
        if (!_variableStore.TryGet(reference, out var handle))
        {
            throw new DebugAdapterException("staleVariables", "The variable reference is no longer available.");
        }

        if (handle.Value.ValueKind != JsonValueKind.Undefined)
        {
            return new { variables = _variableStore.Expand(handle.Value) };
        }

        var body = await bridge.RemoteAsync(
                PluginDebugCommands.Variables,
                new { frameId = handle.FrameId },
                cancellationToken)
            .ConfigureAwait(false);
        var variables = _variableStore.ScopeVariables(body.GetProperty(handle.Scope));
        return new { variables };
    }

    private async ValueTask<object> EvaluateAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var expression = arguments.GetProperty("expression").GetString()!;
        var frameId = Frame(arguments.GetProperty("frameId").GetInt32());
        var body = await bridge.RemoteAsync(
                PluginDebugCommands.Evaluate,
                new { frameId, expression, allowAwait = expression.Contains("await", StringComparison.Ordinal) },
                cancellationToken)
            .ConfigureAwait(false);
        var value = body.GetProperty("value");
        return new { result = DapVariableStore.Display(value), type = value.GetProperty("type").GetString(), variablesReference = _variableStore.ValueReference(value) };
    }

    private ValueTask<object> SetVariableAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var handle = _variableStore.Get(arguments.GetProperty("variablesReference").GetInt32());
        return SetExpressionCoreAsync(
            handle.FrameId,
            arguments.GetProperty("name").GetString()!,
            arguments.GetProperty("value").GetString()!,
            cancellationToken);
    }

    private ValueTask<object> SetExpressionAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        return SetExpressionCoreAsync(
            Frame(arguments.GetProperty("frameId").GetInt32()),
            arguments.GetProperty("expression").GetString()!,
            arguments.GetProperty("value").GetString()!,
            cancellationToken);
    }

    private async ValueTask<object> SetExpressionCoreAsync(
        string frameId,
        string expression,
        string value,
        CancellationToken cancellationToken)
    {
        var body = await bridge.RemoteAsync(
                PluginDebugCommands.SetExpression,
                new { frameId, expression, valueExpression = value },
                cancellationToken)
            .ConfigureAwait(false);
        var result = body.GetProperty("value");
        return new { value = DapVariableStore.Display(result), type = result.GetProperty("type").GetString(), variablesReference = _variableStore.ValueReference(result) };
    }

    private async ValueTask<object> SourceAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var source = Arguments(request).GetProperty("source");
        var path = source.GetProperty("path").GetString()!;
        var response = await bridge.SendAsync(
                "source",
                new Dictionary<string, object?> { ["pluginId"] = pluginId, ["path"] = path },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureBridgeSuccess(response);
        return new { content = response.GetProperty("content").GetString(), mimeType = "text/plain" };
    }

    private async ValueTask<(object? Source, int Line, int Column, int? EndLine, int? EndColumn)> LocationAsync(
        JsonElement frame,
        CancellationToken cancellationToken)
    {
        if (!frame.TryGetProperty("nodeId", out var node) || node.ValueKind != JsonValueKind.String)
        {
            return (null, 1, 1, null, null);
        }

        var response = await bridge.SendAsync(
                "location",
                new Dictionary<string, object?> { ["pluginId"] = pluginId, ["nodeId"] = node.GetString() },
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.GetProperty("success").GetBoolean())
        {
            return (null, 1, 1, null, null);
        }

        var location = response.GetProperty("body");
        var path = Property(location, "Path", "path").GetString()!;
        var source = new { name = Path.GetFileName(path), path, sourceReference = path.StartsWith("dotboxd-ir://", StringComparison.Ordinal) ? 1 : 0 };
        return (
            source,
            Property(location, "Line", "line").GetInt32() + 1,
            Property(location, "Column", "column").GetInt32() + 1,
            OptionalInt(location, "EndLine", "endLine") is { } endLine ? endLine + 1 : null,
            OptionalInt(location, "EndColumn", "endColumn") is { } endColumn ? endColumn + 1 : null);
    }

    private int ThreadId(string runId)
    {
        if (_threadIds.TryGetValue(runId, out var existing))
        {
            return existing;
        }

        var id = Interlocked.Increment(ref _nextThreadId);
        _threadIds[runId] = id;
        _threads[id] = runId;
        return id;
    }

    private string Frame(int frameId)
        => _frames.TryGetValue(frameId, out var frame)
            ? frame
            : throw new DebugAdapterException("staleFrame", "The selected stack frame is no longer stopped.");

    private static JsonElement Arguments(JsonElement request) => request.GetProperty("arguments");

    private static string DapStopReason(string? reason) => reason switch
    {
        "step" => "step",
        "pause" => "pause",
        "exception" or "breakpointConditionError" => "exception",
        _ => "breakpoint"
    };

    private static JsonElement Property(JsonElement value, string first, string second)
        => value.TryGetProperty(first, out var property) ? property : value.GetProperty(second);

    private static int? OptionalInt(JsonElement value, string first, string second)
    {
        var property = Property(value, first, second);
        return property.ValueKind == JsonValueKind.Number ? property.GetInt32() : null;
    }

    private static void EnsureBridgeSuccess(JsonElement response)
    {
        if (!response.GetProperty("success").GetBoolean())
        {
            throw new DebugAdapterException("bridgeError", "The plugin bridge could not serve the requested source.");
        }
    }

}
