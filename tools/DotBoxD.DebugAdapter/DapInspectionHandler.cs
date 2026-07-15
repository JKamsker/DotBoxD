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
    private readonly DapVariableSnapshotStore _variableSnapshots = new();

    public bool Handles(string command) => command is
        "threads" or "stackTrace" or "scopes" or "variables" or "evaluate" or
        "setVariable" or "setExpression" or "completions" or "source";

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
            "completions" => await CompletionsAsync(request, cancellationToken).ConfigureAwait(false),
            "source" => await DapSourceLoader.LoadAsync(bridge, pluginId, request, cancellationToken)
                .ConfigureAwait(false),
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
        var frames = _stopped.RemoveThread(threadId, preserveThreadIdentity);
        _variableStore.RemoveFrames(frames);
        _variableSnapshots.Remove(frames);
    }

    public void InvalidateAllStoppedState()
    {
        _stackTraces.Clear();
        _stopped.Clear();
        _variableStore.Clear();
        _variableSnapshots.Clear();
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

        var context = FrameContext(handle.FrameId);
        var body = await _variableSnapshots.GetAsync(bridge, context, cancellationToken).ConfigureAwait(false);
        var bindings = context.Bindings;
        var projected = DapSourceVariableProjector.Map(
            body.GetProperty(handle.Scope),
            bindings,
            includeSynthetic: handle.Scope == "arguments");
        var variables = _variableStore.ScopeVariables(projected, handle);
        return new { variables };
    }

    private async ValueTask<object> EvaluateAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var expression = arguments.GetProperty("expression").GetString()!;
        var context = FrameContext(arguments.GetProperty("frameId").GetInt32());
        var bindings = context.Bindings;
        var variables = await _variableSnapshots.GetAsync(bridge, context, cancellationToken).ConfigureAwait(false);
        if (DapSourceVariableProjector.TryEvaluate(
                variables.GetProperty("arguments"),
                variables.GetProperty("locals"),
                bindings,
                expression,
                out var sourceValue))
        {
            return new
            {
                result = DapVariableStore.Display(sourceValue),
                type = sourceValue.GetProperty("type").GetString(),
                variablesReference = _variableStore.ValueReference(sourceValue, context.RemoteFrameId)
            };
        }

        var body = await bridge.RemoteAsync(
                PluginDebugCommands.Evaluate,
                new
                {
                    frameId = context.RemoteFrameId,
                    expression = DapSourceVariableProjector.Translate(expression, bindings),
                    allowAwait = IsDebugConsole(arguments)
                },
                cancellationToken)
            .ConfigureAwait(false);
        var value = body.GetProperty("value");
        return new { result = DapVariableStore.Display(value), type = value.GetProperty("type").GetString(), variablesReference = _variableStore.ValueReference(value, context.RemoteFrameId) };
    }

    private async ValueTask<object> CompletionsAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var context = FrameContext(arguments.GetProperty("frameId").GetInt32());
        return new
        {
            targets = DapCompletionBuilder.Build(
                context.Bindings,
                arguments.GetProperty("text").GetString()!,
                arguments.GetProperty("column").GetInt32())
        };
    }

    private ValueTask<object> SetVariableAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var arguments = Arguments(request);
        var handle = _variableStore.Get(arguments.GetProperty("variablesReference").GetInt32());
        var name = arguments.GetProperty("name").GetString()!;
        var target = DapVariableTargetResolver.Resolve(
            handle, name, FrameContext(handle.FrameId).Bindings, _variableStore);
        return SetExpressionCoreAsync(
            handle.FrameId,
            target.Expression,
            arguments.GetProperty("value").GetString()!,
            cancellationToken,
            target.Path);
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
                new
                {
                    frameId,
                    expression = DapSourceVariableProjector.Translate(
                        expression,
                        FrameContext(frameId).Bindings),
                    valueExpression = DapSourceVariableProjector.Translate(value, FrameContext(frameId).Bindings),
                    path
                },
                cancellationToken)
            .ConfigureAwait(false);
        var result = body.GetProperty("value");
        _variableSnapshots.Remove(frameId);
        return new { value = DapVariableStore.Display(result), type = result.GetProperty("type").GetString(), variablesReference = _variableStore.ValueReference(result, frameId) };
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

    private DapFrameContext FrameContext(int frameId) => _stopped.FrameContext(frameId);

    private DapFrameContext FrameContext(string remoteFrameId)
        => _stopped.FrameContext(_stopped.DapFrameId(remoteFrameId));

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
            foreach (var frame in _stopped.FrameContexts(threadId))
            {
                _ = await _variableSnapshots.GetAsync(bridge, frame, CancellationToken.None).ConfigureAwait(false);
            }
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
