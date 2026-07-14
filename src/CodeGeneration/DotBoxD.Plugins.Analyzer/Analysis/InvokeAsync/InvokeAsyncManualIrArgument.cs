using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncManualIrArgument
{
    public static bool IsExplicit(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetOperation(invocation, cancellationToken) is IInvocationOperation operation)
        {
            return HasExplicitBoundArgument(operation, model, cancellationToken);
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: "irInvocation" })
            {
                return IsExplicitValue(argument.Expression, model, cancellationToken);
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var type = model.GetTypeInfo(argument.Expression, cancellationToken);
            if ((type.Type is not null && InvokeAsyncServerSurface.IsIRInvocation(type.Type)) ||
                (type.ConvertedType is not null && InvokeAsyncServerSurface.IsIRInvocation(type.ConvertedType)) ||
                HasImplicitIrConversion(type.Type))
            {
                return IsExplicitValue(argument.Expression, model, cancellationToken);
            }
        }

        return false;
    }

    private static bool IsExplicitValue(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => HasImplicitIrConversion(model.GetTypeInfo(expression, cancellationToken).Type) ||
           !InvokeAsyncArgumentSyntax.IsNullLike(expression, model, cancellationToken);

    private static bool HasImplicitIrConversion(ITypeSymbol? sourceType)
    {
        if (sourceType is not INamedTypeSymbol named)
        {
            return false;
        }

        foreach (var member in named.GetMembers(WellKnownMemberNames.ImplicitConversionName))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Conversion } conversion &&
                InvokeAsyncServerSurface.IsIRInvocation(conversion.ReturnType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExplicitBoundArgument(
        IInvocationOperation operation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var argument in operation.Arguments)
        {
            if (argument.IsImplicit ||
                argument.Parameter is null ||
                !InvokeAsyncServerSurface.IsIRInvocation(argument.Parameter.Type))
            {
                continue;
            }

            var expression = argument.Syntax is ArgumentSyntax syntax
                ? syntax.Expression
                : argument.Value.Syntax as ExpressionSyntax;
            return expression is not null &&
                   IsExplicitValue(expression, model, cancellationToken);
        }

        return false;
    }
}
