using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Debugging;

internal static class SandboxDebugExpressionEvaluator
{
    public static PluginDebugEvaluationResult Evaluate(PluginDebugEvaluationRequest request)
    {
        if (request.AllowAwait)
        {
            return Failure("The SandboxOnly evaluator does not support await.");
        }

        try
        {
            var variables = Variables(request.Frame);
            return PluginDebugEvaluationResult.Success(
                new SandboxDebugExpressionParser(request.Expression, variables).Parse());
        }
        catch (SandboxDebugExpressionException exception)
        {
            return Failure(exception.Message);
        }
        catch (SandboxRuntimeException exception)
        {
            return PluginDebugEvaluationResult.Failure(exception.Error);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return Failure(exception.Message);
        }
    }

    private static IReadOnlyDictionary<string, SandboxValue> Variables(ISandboxDebugFrame frame)
    {
        var variables = new Dictionary<string, SandboxValue>(StringComparer.Ordinal);
        foreach (var variable in frame.Arguments.Concat(frame.Locals))
        {
            if (variable.IsAssigned)
            {
                variables[variable.Name] = variable.Value!;
            }
        }

        return variables;
    }

    private static PluginDebugEvaluationResult Failure(string message)
        => PluginDebugEvaluationResult.Failure(new SandboxError(SandboxErrorCode.InvalidInput, message));
}
