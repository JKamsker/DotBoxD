using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedBuilderResolver
{
    public static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);
        receiverType = null!;
        if (TryResolveGeneratedBuilderComposition(model, receiver, cancellationToken, out receiverType) ||
            TryResolveGeneratedFacadeExpression(model, receiver, cancellationToken, out receiverType) ||
            TryResolveGeneratedBuilderExpression(model, receiver, cancellationToken, out receiverType) ||
            TryResolveGeneratedBuilderProjection(model, receiver, cancellationToken, out receiverType))
        {
            return true;
        }

        if (receiver is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                } &&
                TryResolve(model, initializer, cancellationToken, out receiverType))
            {
                return true;
            }
        }

        return TryResolveDeconstructionLocal(model, identifier, local, cancellationToken, out receiverType);
    }

    private static bool TryResolveGeneratedBuilderComposition(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (receiver is ConditionalExpressionSyntax conditional)
        {
            return TryResolveMatchingTypes(
                model,
                conditional.WhenTrue,
                conditional.WhenFalse,
                cancellationToken,
                out receiverType);
        }

        if (receiver is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression))
        {
            return TryResolveMatchingTypes(
                model,
                coalesce.Left,
                coalesce.Right,
                cancellationToken,
                out receiverType);
        }

        return false;
    }

    private static bool TryResolveGeneratedFacadeExpression(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        var semanticType = model.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
        receiverType = semanticType!;
        return semanticType is not null && InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(semanticType);
    }

    private static bool TryResolveMatchingTypes(
        SemanticModel model,
        ExpressionSyntax left,
        ExpressionSyntax right,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (!TryResolve(model, left, cancellationToken, out var leftType) ||
            !TryResolve(model, right, cancellationToken, out var rightType) ||
            !SymbolEqualityComparer.Default.Equals(leftType, rightType))
        {
            return false;
        }

        receiverType = leftType;
        return true;
    }

    private static bool TryResolveDeconstructionLocal(
        SemanticModel model,
        IdentifierNameSyntax identifier,
        ILocalSymbol local,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is SingleVariableDesignationSyntax designation &&
                !HookChainAliasResolver.HasMutationBetween(
                    local,
                    designation.SpanStart,
                    identifier.SpanStart,
                    model,
                    cancellationToken,
                    designation.SyntaxTree.GetRoot(cancellationToken),
                    nestedFunctionPathNode: identifier) &&
                DeconstructionInitializer(designation, cancellationToken) is { } initializer &&
                TryResolveGeneratedBuilderExpression(model, initializer, cancellationToken, out receiverType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveGeneratedBuilderExpression(
        SemanticModel model,
        ExpressionSyntax expression,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        receiverType = null!;
        if (expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Services",
                Expression: { } facadeExpression
            })
        {
            expression = HookChainAliasResolver.UnwrapTransparentExpression(facadeExpression);
        }

        if (!InvokeAsyncGeneratedBuilderSyntax.TryFacadeNameFromBuilderInitializer(
                expression,
                out var builderType,
                out var facadeName) &&
            (!InvokeAsyncGeneratedBuilderAliasResolver.TryBuilderType(
                model,
                expression,
                cancellationToken,
                out builderType) ||
             !InvokeAsyncGeneratedBuilderSyntax.TryFacadeNameFromBuilderType(builderType, out facadeName)))
        {
            return false;
        }

        return InvokeAsyncGeneratedBuilderSyntax.TryFindGeneratedFacade(
            model,
            builderType,
            facadeName,
            cancellationToken,
            out receiverType);
    }

    private static bool TryResolveGeneratedBuilderProjection(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (receiver is not MemberAccessExpressionSyntax access ||
            !InvokeAsyncGeneratedBuilderSyntax.TryTupleElementIndex(access, model, cancellationToken, out var index) ||
            InvokeAsyncGeneratedBuilderSyntax.TupleElementInitializer(
                access.Expression,
                index,
                model,
                cancellationToken) is not { } initializer)
        {
            return false;
        }

        return TryResolveGeneratedBuilderExpression(model, initializer, cancellationToken, out receiverType);
    }

    private static ExpressionSyntax? DeconstructionInitializer(
        SingleVariableDesignationSyntax designation,
        CancellationToken cancellationToken)
    {
        if (!TryDeconstructionPath(designation, out var declaration, out var path) ||
            declaration.Parent is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return null;
        }

        var current = assignment.Right;
        for (var pathIndex = path.Count - 1; pathIndex >= 0; pathIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            var index = path[pathIndex];
            if (current is not TupleExpressionSyntax tuple || index >= tuple.Arguments.Count)
            {
                return null;
            }

            current = tuple.Arguments[index].Expression;
        }

        return current;
    }

    private static bool TryDeconstructionPath(
        SingleVariableDesignationSyntax designation,
        out DeclarationExpressionSyntax declaration,
        out List<int> path)
    {
        path = [];
        declaration = null!;
        VariableDesignationSyntax current = designation;
        while (current.Parent is ParenthesizedVariableDesignationSyntax variables)
        {
            var index = variables.Variables.IndexOf(current);
            if (index < 0)
            {
                return false;
            }

            path.Add(index);
            switch (variables.Parent)
            {
                case DeclarationExpressionSyntax root:
                    declaration = root;
                    return true;
                case ParenthesizedVariableDesignationSyntax:
                    current = variables;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }
}
