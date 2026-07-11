using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static ResultLocalTerminalShape ResultLocalLambdaParameters(
        InvocationExpressionSyntax invocation,
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var parameters = LambdaParameterNames(lambda);
        if (!HasSupportedResultLocalParameterCount(parameters))
        {
            return default;
        }

        if (ResultLocalDelegateShape(invocation, model, cancellationToken) is { } shape)
        {
            return ShapeWithLambdaParameterNames(shape, parameters);
        }

        return InferResultLocalLambdaShape(lambda, parameters, model, cancellationToken);
    }

    private static bool HasSupportedResultLocalParameterCount(IReadOnlyList<string> parameters)
    {
        if (parameters.Count == 0)
        {
            return false;
        }

        return parameters.Count <= 3;
    }

    private static ResultLocalTerminalShape ShapeWithLambdaParameterNames(
        ResultLocalTerminalShape shape,
        IReadOnlyList<string> parameters)
    {
        if (shape.ContextParam is not null && parameters.Count < 2)
        {
            return default;
        }

        return shape with
        {
            ElementParam = parameters[0],
            ContextParam = shape.ContextParam is null ? null : parameters[1]
        };
    }

    private static ResultLocalTerminalShape InferResultLocalLambdaShape(
        LambdaExpressionSyntax lambda,
        IReadOnlyList<string> parameters,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var asyncLocal = lambda.AsyncKeyword.RawKind != 0 || LambdaReturnsValueTask(lambda, model, cancellationToken);
        if (parameters.Count == 3)
        {
            return ThreeParameterResultLocalShape(lambda, parameters, model, cancellationToken, asyncLocal);
        }

        // With two untyped parameters, "ct" is commonly used for either context or cancellation.
        // Preserve the context shape unless Roslyn or an explicit parameter type proves it is a token.
        if (HasSecondParameterCancellationToken(lambda, parameters, model, cancellationToken))
        {
            return new ResultLocalTerminalShape(parameters[0], null, true, true);
        }

        return new ResultLocalTerminalShape(
            parameters[0],
            parameters.Count == 2 ? parameters[1] : null,
            asyncLocal,
            false);
    }

    private static ResultLocalTerminalShape ThreeParameterResultLocalShape(
        LambdaExpressionSyntax lambda,
        IReadOnlyList<string> parameters,
        SemanticModel model,
        CancellationToken cancellationToken,
        bool asyncLocal)
        => LooksLikeCancellationToken(lambda, index: 2, model, cancellationToken, allowNameFallback: asyncLocal)
            ? new ResultLocalTerminalShape(parameters[0], parameters[1], true, true)
            : default;

    private static bool HasSecondParameterCancellationToken(
        LambdaExpressionSyntax lambda,
        IReadOnlyList<string> parameters,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (parameters.Count != 2)
        {
            return false;
        }

        return LooksLikeCancellationToken(
            lambda,
            index: 1,
            model,
            cancellationToken,
            allowNameFallback: false);
    }

    private static ResultLocalTerminalShape? ResultLocalDelegateShape(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (ResultLocalDelegateInvoke(invocation, model, cancellationToken) is not { } invoke)
        {
            return null;
        }

        var asyncLocal = IsValueTask(invoke.ReturnType);
        return invoke.Parameters.Length switch
        {
            1 => new ResultLocalTerminalShape(null, null, asyncLocal, false),
            2 when IsCancellationToken(invoke.Parameters[1].Type) =>
                new ResultLocalTerminalShape(null, null, true, true),
            2 => new ResultLocalTerminalShape(null, string.Empty, asyncLocal, false),
            3 when IsCancellationToken(invoke.Parameters[2].Type) =>
                new ResultLocalTerminalShape(null, string.Empty, true, true),
            _ => default(ResultLocalTerminalShape?)
        };
    }

    private static IMethodSymbol? ResultLocalDelegateInvoke(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        return FirstParameterDelegateInvoke(method);
    }

    private static IMethodSymbol? FirstParameterDelegateInvoke(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
        {
            return null;
        }

        return method.Parameters[0].Type is INamedTypeSymbol delegateType
            ? delegateType.DelegateInvokeMethod
            : null;
    }

    private static IReadOnlyList<string> LambdaParameterNames(LambdaExpressionSyntax lambda)
        => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter.Identifier.ValueText],
            ParenthesizedLambdaExpressionSyntax parenthesized =>
                parenthesized.ParameterList.Parameters.Select(static p => p.Identifier.ValueText).ToArray(),
            _ => []
        };

    private static bool LooksLikeCancellationToken(
        LambdaExpressionSyntax lambda,
        int index,
        SemanticModel model,
        CancellationToken cancellationToken,
        bool allowNameFallback)
    {
        if (lambda is not ParenthesizedLambdaExpressionSyntax parenthesized ||
            parenthesized.ParameterList.Parameters.Count <= index)
        {
            return false;
        }

        var parameter = parenthesized.ParameterList.Parameters[index];
        if (parameter.Type is not null &&
            model.GetTypeInfo(parameter.Type, cancellationToken).Type is { } type)
        {
            return IsCancellationToken(type);
        }

        if (!allowNameFallback)
        {
            return false;
        }

        var name = parameter.Identifier.ValueText;
        return string.Equals(name, "ct", StringComparison.Ordinal) ||
               string.Equals(name, "token", StringComparison.Ordinal) ||
               string.Equals(name, "cancellationToken", StringComparison.Ordinal);
    }

    private static bool LambdaReturnsValueTask(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol convertedType)
        {
            return convertedType.DelegateInvokeMethod?.ReturnType is { } returnType && IsValueTask(returnType);
        }

        return lambda.ExpressionBody is { } body &&
               (model.GetTypeInfo(body, cancellationToken).ConvertedType ??
                model.GetTypeInfo(body, cancellationToken).Type) is { } bodyType &&
               IsValueTask(bodyType);
    }

    private static bool IsValueTask(ITypeSymbol type)
        => type is INamedTypeSymbol
        {
            Name: "ValueTask",
            ContainingNamespace: { } ns
        } &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);

    private static bool IsCancellationToken(ITypeSymbol type)
        => string.Equals(type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);

    private readonly record struct ResultLocalTerminalShape(
        string? ElementParam,
        string? ContextParam,
        bool AsyncLocal,
        bool HasCancellationToken)
    {
        public static ResultLocalTerminalShape From((string? ElementParam, string? ContextParam) parameters)
            => new(parameters.ElementParam, parameters.ContextParam, false, false);
    }
}
