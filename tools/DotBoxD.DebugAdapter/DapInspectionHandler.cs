using System.Text.Json;
using DotBoxD.Plugins.Debugging;
using static DotBoxD.DebugAdapter.DapInspectionJson;

namespace DotBoxD.DebugAdapter;

internal sealed class DapInspectionHandler(
    DapConnection connection,
    BridgeClient bridge,
    string pluginId)
{
    private readonly DapStoppedExecutionStore _stopped = new();
    private readonly DapVariableStore _variableStore = new();

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
            var threadId = _stopped.RecordThread(runId, envelope.Payload.GetProperty("pluginId").GetString()!);
            var reason = envelope.Payload.GetProperty("reason").GetString();
            await connection.EventAsync(
                    "stopped",
                    new { reason = DapStopReason(reason), threadId, allThreadsStopped = false })
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

    public string RunId(int threadId) => _stopped.RunId(threadId);

    public void InvalidateStoppedState(int threadId)
    {
        _variableStore.RemoveFrames(_stopped.RemoveThread(threadId));
    }

    public void InvalidateAllStoppedState()
    {
        _stopped.Clear();
        _variableStore.Clear();
    }

    private async ValueTask<object> ThreadsAsync(CancellationToken cancellationToken)
    {
        var body = await bridge.RemoteAsync(PluginDebugCommands.Threads, null, cancellationToken).ConfigureAwait(false);
        var threads = body.GetProperty("threads").EnumerateArray()
            .Select(DapThread)
            .ToArray();
        return new { threads };
    }

    private async ValueTask<object> StackTraceAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var threadId = Arguments(request).GetProperty("threadId").GetInt32();
        var runId = RunId(threadId);
        var stoppedPluginId = _stopped.PluginId(threadId, pluginId);
        var body = await bridge.RemoteAsync(
                PluginDebugCommands.StackTrace,
                new { runId },
                cancellationToken)
            .ConfigureAwait(false);
        var frames = new List<object>();
        foreach (var frame in body.GetProperty("frames").EnumerateArray())
        {
            var frameId = _stopped.AddFrame(threadId, frame.GetProperty("frameId").GetString()!);
            var location = await LocationAsync(frame, stoppedPluginId, cancellationToken).ConfigureAwait(false);
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
            return new { variables = _variableStore.Expand(handle) };
        }

        var body = await bridge.RemoteAsync(
                PluginDebugCommands.Variables,
                new { frameId = handle.FrameId },
                cancellationToken)
            .ConfigureAwait(false);
        var variables = _variableStore.ScopeVariables(body.GetProperty(handle.Scope), handle);
        return new { variables };
    }

    private async ValueTask<object> EvaluateAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var expression = arguments.GetProperty("expression").GetString()!;
        var frameId = Frame(arguments.GetProperty("frameId").GetInt32());
        var body = await bridge.RemoteAsync(
                PluginDebugCommands.Evaluate,
                new { frameId, expression, allowAwait = IsDebugConsole(arguments) },
                cancellationToken)
            .ConfigureAwait(false);
        var value = body.GetProperty("value");
        return new { result = DapVariableStore.Display(value), type = value.GetProperty("type").GetString(), variablesReference = _variableStore.ValueReference(value, frameId) };
    }

    private ValueTask<object> SetVariableAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var handle = _variableStore.Get(arguments.GetProperty("variablesReference").GetInt32());
        var name = arguments.GetProperty("name").GetString()!;
        return SetExpressionCoreAsync(
            handle.FrameId,
            handle.VariableName ?? name,
            arguments.GetProperty("value").GetString()!,
            cancellationToken,
            handle.VariableName is null ? null : _variableStore.ChildPath(handle, name));
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
        CancellationToken cancellationToken,
        IReadOnlyList<object>? path = null)
    {
        var body = await bridge.RemoteAsync(
                PluginDebugCommands.SetExpression,
                new { frameId, expression, valueExpression = value, path },
                cancellationToken)
            .ConfigureAwait(false);
        var result = body.GetProperty("value");
        return new { value = DapVariableStore.Display(result), type = result.GetProperty("type").GetString(), variablesReference = _variableStore.ValueReference(result, frameId) };
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
        string stoppedPluginId,
        CancellationToken cancellationToken)
    {
        if (!frame.TryGetProperty("nodeId", out var node) || node.ValueKind != JsonValueKind.String)
        {
            return (null, 1, 1, null, null);
        }

        var response = await bridge.SendAsync(
                "location",
                new Dictionary<string, object?> { ["pluginId"] = stoppedPluginId, ["nodeId"] = node.GetString() },
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
            Property(location, "Line", "line").GetInt32(),
            Property(location, "Column", "column").GetInt32(),
            OptionalInt(location, "EndLine", "endLine"),
            OptionalInt(location, "EndColumn", "endColumn"));
    }

    private object DapThread(JsonElement thread)
    {
        var stoppedPluginId = thread.TryGetProperty("pluginId", out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        var id = _stopped.RecordThread(thread.GetProperty("runId").GetString()!, stoppedPluginId);

        return new { id, name = thread.GetProperty("name").GetString() };
    }

    private string Frame(int frameId) => _stopped.Frame(frameId);

}
