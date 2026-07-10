using System.Text;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Debugging;

internal static class PluginDebugBreakpointEvaluator
{
    public static async ValueTask<string?> StopReasonAsync(
        PluginDebugSession session,
        string pluginId,
        SandboxDebugCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var decision = session.ExecutionState.Decide(pluginId, checkpoint);
        if (decision.ShouldStop)
        {
            return decision.Reason;
        }

        var breakpoint = decision.Breakpoint;
        if (breakpoint is null)
        {
            return null;
        }

        if (breakpoint.Condition is not null)
        {
            var condition = await EvaluateAsync(session, checkpoint.Frame, breakpoint.Condition, cancellationToken)
                .ConfigureAwait(false);
            if (!condition.Succeeded || condition.Value is not BoolValue boolean)
            {
                await PublishEvaluationErrorAsync(session, condition, cancellationToken).ConfigureAwait(false);
                return "breakpointConditionError";
            }

            if (!boolean.Value)
            {
                return null;
            }
        }

        if (breakpoint.LogMessage is null)
        {
            return "breakpoint";
        }

        var output = await RenderLogMessageAsync(
                session,
                checkpoint.Frame,
                breakpoint.LogMessage,
                cancellationToken)
            .ConfigureAwait(false);
        await session.PublishEventAsync("output", new { category = "console", output }, cancellationToken)
            .ConfigureAwait(false);
        return null;
    }

    private static async ValueTask<string> RenderLogMessageAsync(
        PluginDebugSession session,
        ISandboxDebugFrame frame,
        string template,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var open = template.IndexOf('{', position);
            if (open < 0)
            {
                builder.Append(template, position, template.Length - position);
                break;
            }

            builder.Append(template, position, open - position);
            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                builder.Append(template, open, template.Length - open);
                break;
            }

            var expression = template[(open + 1)..close];
            if (string.IsNullOrWhiteSpace(expression) || expression.Length > session.Options.MaxExpressionLength)
            {
                builder.Append("<invalid expression>");
            }
            else
            {
                var result = await EvaluateAsync(session, frame, expression, cancellationToken).ConfigureAwait(false);
                builder.Append(result.Succeeded ? Display(result.Value!) : $"<error: {result.Error?.SafeMessage}>");
            }

            position = close + 1;
        }

        return builder.ToString();
    }

    private static ValueTask<PluginDebugEvaluationResult> EvaluateAsync(
        PluginDebugSession session,
        ISandboxDebugFrame frame,
        string expression,
        CancellationToken cancellationToken)
        => session.Options.EvaluatorProvider.EvaluateAsync(
            new PluginDebugEvaluationRequest(
                frame,
                expression,
                assemblies: session.Assemblies.Snapshot()),
            cancellationToken);

    private static async ValueTask PublishEvaluationErrorAsync(
        PluginDebugSession session,
        PluginDebugEvaluationResult result,
        CancellationToken cancellationToken)
        => await session.PublishEventAsync(
                "output",
                new
                {
                    category = "stderr",
                    output = result.Error?.SafeMessage ?? "Breakpoint condition must evaluate to Bool."
                },
                cancellationToken)
            .ConfigureAwait(false);

    private static string Display(SandboxValue value)
        => value switch
        {
            UnitValue => "unit",
            BoolValue scalar => scalar.Value ? "true" : "false",
            I32Value scalar => scalar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            I64Value scalar => scalar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            F64Value scalar => scalar.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            StringValue scalar => scalar.Value,
            _ => DisplayConstrained(value)
        };

    private static string DisplayConstrained(SandboxValue value)
        => value switch
        {
            GuidValue scalar => scalar.Value.ToString("D"),
            OpaqueIdValue scalar => scalar.Value,
            SandboxPathValue scalar => scalar.Value.RelativePath,
            SandboxUriValue scalar => scalar.Value.Value,
            _ => value.Type.ToString()
        };
}
