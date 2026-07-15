using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    public static InvokeAsyncCallShape? Create(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        return TryCreateMethodLambdaOnly(arguments, method, model, cancellationToken) ??
               TryCreateMethodCaptureBag(arguments, method, model, cancellationToken);
    }

    public static InvokeAsyncCallShape? Create(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol generatedWorldType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        return TryCreateGeneratedLambdaOnly(invocation, generatedWorldType, arguments, model, cancellationToken) ??
               TryCreateGeneratedCaptureBag(invocation, generatedWorldType, arguments, model, cancellationToken);
    }

    private static InvokeAsyncCallShape? TryCreateMethodLambdaOnly(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol method,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (method.TypeArguments.Length != 1)
        {
            return null;
        }

        if (!TrySingleLambdaArgument(arguments, model, cancellationToken, out var lambdaExpression))
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out var lambda))
        {
            return null;
        }

        if (lambda.Body is not BlockSyntax block)
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryWorldParameter(lambda, model, cancellationToken, out var worldType))
        {
            return null;
        }

        return LambdaOnly(lambda, block, worldType, method.TypeArguments[0], model);
    }

    private static InvokeAsyncCallShape? TryCreateMethodCaptureBag(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol method,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (method.TypeArguments.Length != 2)
        {
            return null;
        }

        if (!TryCaptureArguments(arguments, model, cancellationToken, out var capturesExpression, out var lambdaExpression))
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out var lambda))
        {
            return null;
        }

        if (lambda.Body is not BlockSyntax block)
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryCaptureParameter(
                lambda,
                model,
                capturesExpression,
                cancellationToken,
                expectedWorldType: null,
                expectedCaptureType: method.TypeArguments[0],
                out var captureParameter,
                out var worldType))
        {
            return null;
        }

        if (HasExternalCaptures(lambda, model))
        {
            return null;
        }

        return CaptureBag(
            method.TypeArguments[1],
            captureParameter,
            block,
            model,
            worldType,
            InvokeAsyncLambdaShape.WorldParameterName(lambda));
    }

    private static InvokeAsyncCallShape? TryCreateGeneratedLambdaOnly(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol generatedWorldType,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!TrySingleLambdaArgument(arguments, model, cancellationToken, out var lambdaExpression))
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out var lambda))
        {
            return null;
        }

        if (lambda.Body is not BlockSyntax block)
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryWorldParameter(lambda, model, cancellationToken, generatedWorldType, out var worldType))
        {
            return null;
        }

        if (!TryGeneratedReceiverReturnType(
                invocation,
                block,
                model,
                cancellationToken,
                expectedTypeArgumentCount: 1,
                typeArgumentIndex: 0,
                out var returnType))
        {
            return null;
        }

        return LambdaOnly(lambda, block, worldType, returnType, model);
    }

    private static InvokeAsyncCallShape? TryCreateGeneratedCaptureBag(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol generatedWorldType,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!TryCaptureArguments(arguments, model, cancellationToken, out var capturesExpression, out var lambdaExpression))
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryLambda(lambdaExpression, out var lambda))
        {
            return null;
        }

        if (lambda.Body is not BlockSyntax block)
        {
            return null;
        }

        if (!InvokeAsyncLambdaShape.TryCaptureParameter(
                lambda,
                model,
                capturesExpression,
                cancellationToken,
                generatedWorldType,
                ExplicitCaptureType(invocation, model, cancellationToken),
                out var captureParameter,
                out var worldType))
        {
            return null;
        }

        if (!TryGeneratedReceiverReturnType(
                invocation,
                block,
                model,
                cancellationToken,
                expectedTypeArgumentCount: 2,
                typeArgumentIndex: 1,
                out var returnType))
        {
            return null;
        }

        if (HasExternalCaptures(lambda, model))
        {
            return null;
        }

        return CaptureBag(
            returnType,
            captureParameter,
            block,
            model,
            worldType,
            InvokeAsyncLambdaShape.WorldParameterName(lambda));
    }

    private static ITypeSymbol? ExplicitCaptureType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => TryExplicitGenericTypeArgument(
            invocation,
            model,
            cancellationToken,
            expectedTypeArgumentCount: 2,
            typeArgumentIndex: 0,
            out var captureType)
            ? captureType
            : null;
}
