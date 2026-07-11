using System.Text.Json;
using DotBoxD.DebugAdapter.Diagnostics;
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
    private readonly DapStackTraceLoader _stackTraces = new(bridge);
    private readonly DapResumeStopBuffer _resumeStops = new();

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
            var pending = new DapPendingStop(
                envelope.Payload.GetProperty("runId").GetString()!,
                envelope.Payload.GetProperty("pluginId").GetString()!,
                envelope.Payload.GetProperty("reason").GetString());
            if (_resumeStops.TryBuffer(pending))
            {
                return;
            }

            await EmitStoppedAsync(pending).ConfigureAwait(false);
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

    public void BeginResume() => _resumeStops.BeginResume();

    public async ValueTask CompleteResumeAsync()
    {
        foreach (var stop in _resumeStops.CompleteResume())
        {
            await EmitStoppedAsync(stop).ConfigureAwait(false);
        }
    }

    public void InvalidateStoppedState(int threadId, bool preserveThreadIdentity = false)
    {
        _stackTraces.Invalidate(threadId);
        _variableStore.RemoveFrames(_stopped.RemoveThread(threadId, preserveThreadIdentity));
    }

    public void InvalidateAllStoppedState()
    {
        _stackTraces.Clear();
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
        var stoppedPluginId = _stopped.PluginId(threadId, pluginId);
        return await _stackTraces.LoadAsync(
                threadId,
                RunId(threadId),
                stoppedPluginId,
                _stopped,
                cancellationToken)
            .ConfigureAwait(false);
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

    private async ValueTask EmitStoppedAsync(DapPendingStop stop)
    {
        var threadId = _stopped.RecordThread(stop.RunId, stop.PluginId);
        try
        {
            _ = await _stackTraces.LoadAsync(
                    threadId,
                    stop.RunId,
                    stop.PluginId,
                    _stopped,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DebugAdapterException or IOException or InvalidDataException)
        {
            AdapterDiagnostics.Write("stack prefetch failed: " + exception.Message);
        }

        await connection.EventAsync(
                "stopped",
                new { reason = DapStopReason(stop.Reason), threadId, allThreadsStopped = false })
            .ConfigureAwait(false);
    }

}
