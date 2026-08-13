using System.Text.Json;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugEvaluationRequestHandler(PluginDebugSession session)
{
    public bool Handles(string command)
        => command is PluginDebugCommands.Evaluate or PluginDebugCommands.SetExpression;

    public async ValueTask<PluginDebugHandlerResult> HandleAsync(
        PluginDebugEnvelope request,
        CancellationToken cancellationToken)
    {
        if (!TryReadString(request.Payload, "frameId", out var frameId) ||
            !session.ExecutionState.TryGetFrame(frameId!, out var frame, out var pluginId))
        {
            return PluginDebugHandlerResult.Error("staleFrame", "The requested frame is not stopped.");
        }

        return request.Kind == PluginDebugCommands.Evaluate
            ? await EvaluateAsync(request.Payload, frame!, pluginId!, cancellationToken).ConfigureAwait(false)
            : await SetExpressionAsync(request.Payload, frame!, pluginId!, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PluginDebugHandlerResult> EvaluateAsync(
        JsonElement payload,
        Kernels.Debugging.ISandboxDebugFrame frame,
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpression(payload, "expression", out var expression, out var error))
        {
            return error!;
        }

        if (PluginDebugSourceVariables.TryEvaluate(
                frame.Arguments.Concat(frame.Locals).ToArray(),
                session.DebugInfo(pluginId),
                frame.FunctionId,
                expression!,
                out var sourceValue))
        {
            return PluginDebugHandlerResult.Ok(new { value = sourceValue });
        }

        var allowAwait = payload.TryGetProperty("allowAwait", out var awaitValue) &&
            awaitValue.ValueKind == JsonValueKind.True;
        var result = await EvaluateCoreAsync(frame, expression!, allowAwait, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? PluginDebugHandlerResult.Ok(new { value = PluginDebugValueCodec.Snapshot(result.Value!) })
            : EvaluationError(result);
    }

    private async ValueTask<PluginDebugHandlerResult> SetExpressionAsync(
        JsonElement payload,
        Kernels.Debugging.ISandboxDebugFrame frame,
        string pluginId,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpression(payload, "expression", out var target, out var targetError))
        {
            return targetError!;
        }

        if (!TryReadExpression(payload, "valueExpression", out var valueExpression, out var valueError))
        {
            return valueError!;
        }

        var binding = session.DebugInfo(pluginId)?.VariableBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.FunctionId, frame.FunctionId, StringComparison.Ordinal) &&
            string.Equals(candidate.SourceName, target, StringComparison.Ordinal));
        var slotName = binding?.DisplayValue is null ? binding?.SlotName ?? target : target;
        var variable = frame.Arguments.Concat(frame.Locals)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, slotName, StringComparison.Ordinal));
        if (variable is null)
        {
            return PluginDebugHandlerResult.Error(
                "invalidExpression",
                "setExpression targets must name an existing sandbox variable.");
        }

        var result = await EvaluateCoreAsync(frame, valueExpression!, allowAwait: false, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return EvaluationError(result);
        }

        IReadOnlyList<Kernels.Debugging.SandboxDebugValuePathSegment> path;
        try
        {
            path = PluginDebugValuePathParser.Parse(payload, variable.Type);
        }
        catch (ArgumentException exception)
        {
            return PluginDebugHandlerResult.Error("invalidWrite", exception.Message);
        }

        var written = path.Count == 0
            ? frame.TrySetVariable(variable.Name, result.Value!, out var writeError)
            : frame.TrySetMember(variable.Name, path, result.Value!, out writeError);
        if (!written)
        {
            return PluginDebugHandlerResult.Error(
                "invalidWrite",
                writeError?.SafeMessage ?? "The sandbox rejected the expression write.");
        }

        return PluginDebugHandlerResult.Ok(new { value = PluginDebugValueCodec.Snapshot(result.Value!) });
    }

    private async ValueTask<PluginDebugEvaluationResult> EvaluateCoreAsync(
        Kernels.Debugging.ISandboxDebugFrame frame,
        string expression,
        bool allowAwait,
        CancellationToken cancellationToken)
    {
        var evaluator = session.Options.EvaluatorProvider;
        if (allowAwait && !evaluator.SupportsAwait)
        {
            return PluginDebugEvaluationResult.Failure(new Kernels.Sandbox.SandboxError(
                Kernels.Sandbox.SandboxErrorCode.InvalidInput,
                $"Evaluator '{evaluator.Id}' does not support await."));
        }

        return await evaluator.EvaluateAsync(
                new PluginDebugEvaluationRequest(
                    frame,
                    expression,
                    allowAwait,
                    session.Assemblies.Snapshot()),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryReadExpression(
        JsonElement payload,
        string name,
        out string? expression,
        out PluginDebugHandlerResult? error)
    {
        if (!TryReadString(payload, name, out expression))
        {
            error = PluginDebugHandlerResult.Error("invalidArguments", $"A non-empty {name} is required.");
            return false;
        }

        if (expression!.Length > session.Options.MaxExpressionLength)
        {
            error = PluginDebugHandlerResult.Error(
                "expressionTooLong",
                $"The expression exceeds the {session.Options.MaxExpressionLength}-character host limit.");
            return false;
        }

        error = null;
        return true;
    }

    private static PluginDebugHandlerResult EvaluationError(PluginDebugEvaluationResult result)
        => PluginDebugHandlerResult.Error(
            "evaluationFailed",
            result.Error?.SafeMessage ?? "The evaluator rejected the expression.");

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
}
