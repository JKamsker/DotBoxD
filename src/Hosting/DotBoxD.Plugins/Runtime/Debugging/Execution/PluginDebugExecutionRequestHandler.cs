using System.Text.Json;
using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugExecutionRequestHandler(PluginDebugSession session)
{
    private readonly PluginDebugBreakpointParser _breakpoints = new(session.Options.MaxExpressionLength);
    private readonly PluginDebugEvaluationRequestHandler _evaluation = new(session);

    public async ValueTask<PluginDebugHandlerResult?> HandleAsync(
        PluginDebugEnvelope request,
        CancellationToken cancellationToken)
    {
        if (_evaluation.Handles(request.Kind))
        {
            return await _evaluation.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var result = request.Kind switch
        {
            PluginDebugCommands.SetBreakpoints => SetBreakpoints(request.Payload),
            PluginDebugCommands.Pause => Pause(),
            PluginDebugCommands.Threads => Threads(),
            PluginDebugCommands.StackTrace => StackTrace(request.Payload),
            PluginDebugCommands.Variables => Variables(request.Payload),
            PluginDebugCommands.Completions => Completions(request.Payload),
            PluginDebugCommands.SetVariable => SetVariable(request.Payload),
            _ => ResumeCommand(request)
        };
        return result;
    }

    private PluginDebugHandlerResult? ResumeCommand(PluginDebugEnvelope request)
        => request.Kind switch
        {
            PluginDebugCommands.Continue => Resume(request.Payload, PluginDebugStepKind.Continue),
            PluginDebugCommands.StepIn => Resume(request.Payload, PluginDebugStepKind.StepIn),
            PluginDebugCommands.StepOver => Resume(request.Payload, PluginDebugStepKind.StepOver),
            PluginDebugCommands.StepOut => Resume(request.Payload, PluginDebugStepKind.StepOut),
            _ => null
        };

    private PluginDebugHandlerResult SetBreakpoints(JsonElement payload)
    {
        if (!TryReadString(payload, "pluginId", out var pluginId) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return PluginDebugHandlerResult.Error(
                "invalidArguments",
                "setBreakpoints requires pluginId and a breakpoint array.");
        }

        try
        {
            var parsed = _breakpoints.Parse(payload);
            session.ExecutionState.SetBreakpoints(pluginId!, parsed);
            return PluginDebugHandlerResult.Ok(new
            {
                breakpoints = parsed.Select(breakpoint => new
                {
                    nodeId = breakpoint.NodeId.Value,
                    verified = session.IsBreakpointVerified(pluginId!, breakpoint.NodeId)
                }).ToArray()
            });
        }
        catch (ArgumentException exception)
        {
            return PluginDebugHandlerResult.Error("invalidBreakpoint", exception.Message);
        }
    }

    private PluginDebugHandlerResult Pause()
    {
        if (!session.IsAttached)
        {
            return PluginDebugHandlerResult.Error("notAttached", "No debugger is attached to this session.");
        }

        session.ExecutionState.RequestPause();
        return PluginDebugHandlerResult.Ok(new { });
    }

    private PluginDebugHandlerResult Resume(JsonElement payload, PluginDebugStepKind stepKind)
    {
        if (!TryReadString(payload, "runId", out var runId) ||
            !session.ExecutionState.PrepareResume(runId!, stepKind))
        {
            return StaleRun();
        }

        return session.Resume(runId!)
            ? PluginDebugHandlerResult.Ok(new { runId })
            : StaleRun();
    }

    private PluginDebugHandlerResult Threads()
    {
        var threads = session.ExecutionState.StoppedExecutions()
            .Select(execution => new
            {
                runId = execution.Checkpoint.RunId.ToString(),
                pluginId = execution.PluginId,
                name = execution.PluginId + ":" + execution.Checkpoint.Frame.FunctionId,
                reason = execution.Reason
            })
            .ToArray();
        return PluginDebugHandlerResult.Ok(new { threads });
    }

    private PluginDebugHandlerResult StackTrace(JsonElement payload)
    {
        if (!TryReadString(payload, "runId", out var runId) ||
            !session.ExecutionState.TryGetStopped(runId!, out var execution))
        {
            return StaleRun();
        }

        var stopped = execution!;
        var frames = new List<object>();
        for (var frame = stopped.Checkpoint.Frame; frame is not null; frame = frame.Caller)
        {
            frames.Add(new
            {
                frameId = FrameId(runId!, frame.Depth),
                pluginId = stopped.PluginId,
                functionId = frame.FunctionId,
                depth = frame.Depth,
                nodeId = ReferenceEquals(frame, stopped.Checkpoint.Frame)
                    ? stopped.Checkpoint.Node.Id.Value
                    : null
            });
        }

        return PluginDebugHandlerResult.Ok(new { frames });
    }

    private PluginDebugHandlerResult Variables(JsonElement payload)
    {
        if (!TryReadString(payload, "frameId", out var frameId) ||
            !session.ExecutionState.TryGetFrame(frameId!, out var frame, out var pluginId))
        {
            return PluginDebugHandlerResult.Error("staleFrame", "The requested frame is not stopped.");
        }

        var stoppedFrame = frame!;
        var debugInfo = session.DebugInfo(pluginId!);
        return PluginDebugHandlerResult.Ok(new
        {
            arguments = PluginDebugSourceVariables.Map(
                stoppedFrame.Arguments,
                debugInfo,
                stoppedFrame.FunctionId,
                SandboxDebugVariableKind.Argument),
            locals = PluginDebugSourceVariables.Map(
                stoppedFrame.Locals,
                debugInfo,
                stoppedFrame.FunctionId,
                SandboxDebugVariableKind.Local)
        });
    }

    private PluginDebugHandlerResult Completions(JsonElement payload)
    {
        if (!TryReadString(payload, "frameId", out var frameId) ||
            !session.ExecutionState.TryGetFrame(frameId!, out var frame, out var pluginId))
        {
            return PluginDebugHandlerResult.Error("staleFrame", "The requested frame is not stopped.");
        }

        var paths = PluginDebugSourceVariables.CompletionPaths(
            session.DebugInfo(pluginId!),
            frame!.FunctionId);
        return PluginDebugHandlerResult.Ok(new { paths });
    }

    private PluginDebugHandlerResult SetVariable(JsonElement payload)
    {
        if (!TryReadString(payload, "frameId", out var frameId) ||
            !TryReadString(payload, "name", out var name) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("value", out var encoded) ||
            !session.ExecutionState.TryGetFrame(frameId!, out var frame))
        {
            return PluginDebugHandlerResult.Error("staleFrame", "setVariable requires a stopped frame, name, and value.");
        }

        var stoppedFrame = frame!;
        var variable = stoppedFrame.Arguments.Concat(stoppedFrame.Locals)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (variable is null)
        {
            return PluginDebugHandlerResult.Error("unknownVariable", $"Debug variable '{name}' does not exist.");
        }

        if (!PluginDebugValueCodec.TryParse(encoded, variable.Type, out var value, out var parseError))
        {
            return PluginDebugHandlerResult.Error("invalidValue", parseError!);
        }

        if (!stoppedFrame.TrySetVariable(name!, value!, out var writeError))
        {
            return PluginDebugHandlerResult.Error(
                "invalidWrite",
                writeError?.SafeMessage ?? "The sandbox rejected the variable write.");
        }

        var updated = new SandboxDebugVariable(variable.Name, variable.Type, variable.Kind, true, value);
        return PluginDebugHandlerResult.Ok(new { variable = SnapshotVariable(updated) });
    }

    private static object SnapshotVariable(SandboxDebugVariable variable)
        => new
        {
            name = variable.Name,
            kind = variable.Kind.ToString(),
            type = variable.Type.ToString(),
            assigned = variable.IsAssigned,
            value = variable.Value is null ? null : PluginDebugValueCodec.Snapshot(variable.Value)
        };

    private static string FrameId(string runId, int depth)
        => runId + ":" + depth.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool TryReadString(JsonElement payload, string name, out string? value)
    {
        value = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static PluginDebugHandlerResult StaleRun()
        => PluginDebugHandlerResult.Error("staleRun", "The requested execution is not stopped in this session.");
}

internal sealed record PluginDebugHandlerResult(bool Succeeded, object? Body, string? Code, string? Message)
{
    public static PluginDebugHandlerResult Ok(object body) => new(true, body, null, null);

    public static PluginDebugHandlerResult Error(string code, string message) => new(false, null, code, message);
}
