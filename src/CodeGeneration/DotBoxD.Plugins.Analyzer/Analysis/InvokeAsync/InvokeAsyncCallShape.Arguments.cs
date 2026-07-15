using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static bool TrySingleLambdaArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ExpressionSyntax lambda)
    {
        lambda = null!;
        if (arguments.Count is < 1 or > 3)
        {
            return false;
        }

        var state = new LambdaArgumentState(model, cancellationToken);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (!state.TryAssign(LambdaArgumentName(argument, i), argument.Expression))
            {
                return false;
            }
        }

        return state.TryGet(out lambda);
    }

    private static string LambdaArgumentName(ArgumentSyntax argument, int index)
        => argument.NameColon?.Name.Identifier.ValueText ??
           index switch
           {
               0 => "lambda",
               1 => "irInvocation",
               _ => "cancellationToken"
           };

    private static bool TryCaptureArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ExpressionSyntax captures,
        out ExpressionSyntax lambda)
    {
        captures = null!;
        lambda = null!;
        if (arguments.Count is < 2 or > 4)
        {
            return false;
        }

        var state = new CaptureArgumentState(model, cancellationToken);
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
               2 => "irInvocation",
               _ => "cancellationToken"
           };

    private sealed class CaptureArgumentState
    {
        private readonly SemanticModel _model;
        private readonly CancellationToken _cancellationToken;
        private ExpressionSyntax? _captures;
        private ExpressionSyntax? _lambda;
        private bool _assignedIr;
        private bool _assignedCancellation;

        public CaptureArgumentState(SemanticModel model, CancellationToken cancellationToken)
        {
            _model = model;
            _cancellationToken = cancellationToken;
        }

        public bool TryAssign(string name, ExpressionSyntax expression)
            => name switch
            {
                "captures" => AssignOnce(ref _captures, expression),
                "lambda" => AssignOnce(ref _lambda, expression),
                "irInvocation" => TryAssignIr(expression),
                "cancellationToken" => TryAssignCancellation(),
                _ => false
            };

        public bool TryGet(out ExpressionSyntax captures, out ExpressionSyntax lambda)
        {
            captures = _captures!;
            lambda = _lambda!;
            return _captures is not null && _lambda is not null;
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

        private bool TryAssignIr(ExpressionSyntax expression)
        {
            if (_assignedIr || !InvokeAsyncArgumentSyntax.IsNullLike(expression, _model, _cancellationToken))
            {
                return false;
            }

            _assignedIr = true;
            return true;
        }
    }

    private sealed class LambdaArgumentState
    {
        private readonly SemanticModel _model;
        private readonly CancellationToken _cancellationToken;
        private ExpressionSyntax? _lambda;
        private bool _assignedIr;
        private bool _assignedCancellation;

        public LambdaArgumentState(SemanticModel model, CancellationToken cancellationToken)
        {
            _model = model;
            _cancellationToken = cancellationToken;
        }

        public bool TryAssign(string name, ExpressionSyntax expression)
            => name switch
            {
                "lambda" => AssignOnce(ref _lambda, expression),
                "irInvocation" => TryAssignIr(expression),
                "cancellationToken" => TryAssignCancellation(),
                _ => false
            };

        public bool TryGet(out ExpressionSyntax lambda)
        {
            lambda = _lambda!;
            return _lambda is not null;
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

        private bool TryAssignIr(ExpressionSyntax expression)
        {
            if (_assignedIr || !InvokeAsyncArgumentSyntax.IsNullLike(expression, _model, _cancellationToken))
            {
                return false;
            }

            _assignedIr = true;
            return true;
        }
    }

    private static bool AssignOnce(ref ExpressionSyntax? target, ExpressionSyntax expression)
    {
        if (target is not null)
        {
            return false;
        }

        target = expression;
        return true;
    }
}
