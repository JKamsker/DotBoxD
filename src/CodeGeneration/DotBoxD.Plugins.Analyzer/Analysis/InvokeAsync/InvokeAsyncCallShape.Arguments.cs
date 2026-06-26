using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static bool TrySingleLambdaArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        out ExpressionSyntax lambda)
    {
        lambda = null!;
        if (arguments.Count is < 1 or > 2)
        {
            return false;
        }

        var assignedLambda = false;
        var assignedCancellation = false;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText ?? (i == 0 ? "lambda" : "cancellationToken");
            if (string.Equals(name, "lambda", StringComparison.Ordinal))
            {
                if (assignedLambda)
                {
                    return false;
                }

                lambda = argument.Expression;
                assignedLambda = true;
                continue;
            }

            if (string.Equals(name, "cancellationToken", StringComparison.Ordinal))
            {
                if (assignedCancellation)
                {
                    return false;
                }

                assignedCancellation = true;
                continue;
            }

            return false;
        }

        return assignedLambda;
    }

    private static bool TryCaptureArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        out ExpressionSyntax captures,
        out ExpressionSyntax lambda)
    {
        captures = null!;
        lambda = null!;
        if (arguments.Count is < 2 or > 3)
        {
            return false;
        }

        var assignedCaptures = false;
        var assignedLambda = false;
        var assignedCancellation = false;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText;
            if (name is null)
            {
                name = i switch
                {
                    0 => "captures",
                    1 => "lambda",
                    _ => "cancellationToken"
                };
            }

            if (string.Equals(name, "captures", StringComparison.Ordinal))
            {
                if (assignedCaptures)
                {
                    return false;
                }

                captures = argument.Expression;
                assignedCaptures = true;
                continue;
            }

            if (string.Equals(name, "lambda", StringComparison.Ordinal))
            {
                if (assignedLambda)
                {
                    return false;
                }

                lambda = argument.Expression;
                assignedLambda = true;
                continue;
            }

            if (string.Equals(name, "cancellationToken", StringComparison.Ordinal))
            {
                if (assignedCancellation)
                {
                    return false;
                }

                assignedCancellation = true;
                continue;
            }

            return false;
        }

        return assignedCaptures && assignedLambda;
    }
}
