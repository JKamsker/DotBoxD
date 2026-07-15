using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedBuilderResolver
{
    private const string BuilderSuffix = "Builder";

    public static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);
        receiverType = null!;
        if (TryResolveGeneratedBuilderExpression(model, receiver, cancellationToken, out receiverType))
        {
            return true;
        }

        if (TryResolveGeneratedBuilderProjection(model, receiver, cancellationToken, out receiverType))
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
                TryResolveGeneratedBuilderExpression(model, initializer, cancellationToken, out receiverType))
            {
                return true;
            }
        }

        return TryResolveDeconstructionLocal(model, identifier, local, cancellationToken, out receiverType);
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
                TryFacadeNameFromBuilderInitializer(initializer, out var facadeName) &&
                TryFindGeneratedFacade(model.Compilation, facadeName, cancellationToken, out receiverType))
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

        if (!TryFacadeNameFromBuilderInitializer(expression, out var builderType, out var facadeName) &&
            (!InvokeAsyncGeneratedBuilderAliasResolver.TryBuilderType(
                model,
                expression,
                cancellationToken,
                out builderType) ||
             !TryFacadeNameFromBuilderType(builderType, out facadeName)))
        {
            return false;
        }

        return TryFindGeneratedFacade(
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
            !TryTupleElementIndex(access, model, cancellationToken, out var index) ||
            TupleElementInitializer(access.Expression, index, model, cancellationToken) is not { } initializer)
        {
            return false;
        }

        return TryResolveGeneratedBuilderExpression(model, initializer, cancellationToken, out receiverType);
    }

    private static bool TryFacadeNameFromBuilderInitializer(
        ExpressionSyntax initializer,
        out ExpressionSyntax builderType,
        out string facadeName)
    {
        builderType = null!;
        facadeName = string.Empty;
        return initializer is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Build",
                Expression: { } buildReceiver
            }
        } && TryFacadeNameFromBuilderFactory(buildReceiver, out builderType, out facadeName);
    }

    private static bool TryFacadeNameFromBuilderFactory(
        ExpressionSyntax buildReceiver,
        out ExpressionSyntax builderType,
        out string facadeName)
    {
        builderType = null!;
        facadeName = string.Empty;
        var current = buildReceiver;
        while (current is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: { } next }
            })
        {
            if (TryFacadeNameFromBuilderType(next, out facadeName))
            {
                builderType = next;
                return true;
            }

            current = next;
        }

        if (!TryFacadeNameFromBuilderType(current, out facadeName))
        {
            return false;
        }

        builderType = current;
        return true;
    }

    private static bool TryFacadeNameFromBuilderType(
        ExpressionSyntax builderType,
        out string facadeName)
    {
        var builderName = builderType switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };

        if (!builderName.EndsWith(BuilderSuffix, StringComparison.Ordinal) ||
            builderName.Length == BuilderSuffix.Length)
        {
            facadeName = string.Empty;
            return false;
        }

        facadeName = builderName.Substring(0, builderName.Length - BuilderSuffix.Length);
        return true;
    }

    private static bool TryFindGeneratedFacade(
        SemanticModel model,
        ExpressionSyntax builderType,
        string facadeName,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        var builderSymbol = model.GetSymbolInfo(builderType, cancellationToken);
        // A builder emitted by this generator run is unresolved in the input compilation. A bound
        // symbol belongs to user or referenced code and must never be inferred from its name.
        if (builderSymbol.Symbol is not null ||
            builderSymbol.CandidateSymbols.Length != 0 ||
            model.GetTypeInfo(builderType, cancellationToken).Type is { TypeKind: not TypeKind.Error })
        {
            return false;
        }

        INamedTypeSymbol? found = null;
        foreach (var symbol in LookupFacadeSymbols(model, builderType, facadeName, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is INamedTypeSymbol candidate &&
                InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(candidate))
            {
                if (found is not null &&
                    !SymbolEqualityComparer.Default.Equals(found, candidate))
                {
                    return false;
                }

                found = candidate;
            }
        }

        receiverType = found!;
        return found is not null;
    }

    private static IEnumerable<ISymbol> LookupFacadeSymbols(
        SemanticModel model,
        ExpressionSyntax builderType,
        string facadeName,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax? qualifier = builderType switch
        {
            MemberAccessExpressionSyntax member => member.Expression,
            QualifiedNameSyntax qualified => qualified.Left,
            _ => null
        };
        if (qualifier is null)
        {
            return model.LookupNamespacesAndTypes(builderType.SpanStart, name: facadeName);
        }

        var container = qualifier is IdentifierNameSyntax identifier &&
                        model.GetAliasInfo(identifier, cancellationToken)?.Target is INamespaceOrTypeSymbol aliasTarget
            ? aliasTarget
            : model.GetSymbolInfo(qualifier, cancellationToken).Symbol as INamespaceOrTypeSymbol;
        return container is not null
            ? model.LookupNamespacesAndTypes(builderType.SpanStart, container, facadeName)
            : Enumerable.Empty<ISymbol>();
    }

    private static bool TryTupleElementIndex(
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        out int index)
    {
        if (TryTupleItemNameIndex(access.Name.Identifier.ValueText, out index))
        {
            return true;
        }

        if (model.GetSymbolInfo(access, cancellationToken).Symbol is IFieldSymbol
            {
                CorrespondingTupleField.Name: { } itemName
            })
        {
            return TryTupleItemNameIndex(itemName, out index);
        }

        return false;
    }

    private static bool TryTupleItemNameIndex(string name, out int index)
    {
        index = -1;
        if (!name.StartsWith("Item", StringComparison.Ordinal) ||
            !int.TryParse(name.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out var oneBasedIndex) ||
            oneBasedIndex <= 0)
        {
            return false;
        }

        index = oneBasedIndex - 1;
        return true;
    }

    private static ExpressionSyntax? TupleElementInitializer(
        ExpressionSyntax expression,
        int index,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var tuple = expression as TupleExpressionSyntax;
        if (tuple is null &&
            HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            tuple = HookChainAliasResolver.UnwrapTransparentExpression(initializer) as TupleExpressionSyntax;
        }

        return tuple is not null && index < tuple.Arguments.Count
            ? tuple.Arguments[index].Expression
            : null;
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
        for (var indexIndex = path.Count - 1; indexIndex >= 0; indexIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            var index = path[indexIndex];
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
        path = new List<int>();
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
