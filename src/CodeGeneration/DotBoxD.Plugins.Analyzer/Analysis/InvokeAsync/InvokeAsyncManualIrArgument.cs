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

        return HasExplicitUnboundArgument(invocation.ArgumentList.Arguments, model, cancellationToken);
    }

    private static bool HasExplicitUnboundArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var argument in arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: "irInvocation" })
            {
                return IsExplicitValue(argument.Expression, model, cancellationToken);
            }
        }

        return HasExplicitPositionalArgument(arguments, model, cancellationToken);
    }

    private static bool HasExplicitPositionalArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        for (var index = 1; index <= 2 && index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.NameColon is not null)
            {
                continue;
            }

            var type = model.GetTypeInfo(argument.Expression, cancellationToken);
            if (HasIrTarget(type))
            {
                return IsExplicitValue(argument.Expression, model, cancellationToken);
            }
        }

        return false;
    }

    private static bool HasIrTarget(TypeInfo type)
        => (type.Type is not null && InvokeAsyncServerSurface.IsIRInvocation(type.Type)) ||
           (type.ConvertedType is not null && InvokeAsyncServerSurface.IsIRInvocation(type.ConvertedType)) ||
           HasImplicitIrConversion(type.Type);

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

        for (var declaringType = named; declaringType is not null; declaringType = declaringType.BaseType)
        {
            foreach (var member in declaringType.GetMembers(WellKnownMemberNames.ImplicitConversionName))
            {
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion } conversion &&
                    InvokeAsyncServerSurface.IsIRInvocation(conversion.ReturnType))
                {
                    return true;
                }
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
