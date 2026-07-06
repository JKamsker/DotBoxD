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

        var state = new CaptureArgumentState();
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (!state.TryAssign(CaptureArgumentName(argument, i), argument.Expression))
            {
                return false;
            }
        }

        return state.TryGet(out captures, out lambda);
    }

    private static string CaptureArgumentName(ArgumentSyntax argument, int index)
        => argument.NameColon?.Name.Identifier.ValueText ??
           index switch
           {
               0 => "captures",
               1 => "lambda",
               _ => "cancellationToken"
           };

    private sealed class CaptureArgumentState
    {
        private ExpressionSyntax? _captures;
        private ExpressionSyntax? _lambda;
        private bool _assignedCancellation;

        public bool TryAssign(string name, ExpressionSyntax expression)
            => name switch
            {
                "captures" => TryAssignOnce(ref _captures, expression),
                "lambda" => TryAssignOnce(ref _lambda, expression),
                "cancellationToken" => TryAssignCancellation(),
                _ => false
            };

        public bool TryGet(out ExpressionSyntax captures, out ExpressionSyntax lambda)
        {
            captures = _captures!;
            lambda = _lambda!;
            return _captures is not null && _lambda is not null;
        }

        private static bool TryAssignOnce(ref ExpressionSyntax? target, ExpressionSyntax expression)
        {
            if (target is not null)
            {
                return false;
            }

            target = expression;
            return true;
        }

        private bool TryAssignCancellation()
        {
            if (_assignedCancellation)
            {
                return false;
            }

            _assignedCancellation = true;
            return true;
        }
    }
}
