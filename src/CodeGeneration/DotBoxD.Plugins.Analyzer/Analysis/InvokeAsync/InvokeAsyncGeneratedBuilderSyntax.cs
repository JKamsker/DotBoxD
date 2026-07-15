using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedBuilderSyntax
{
    private const string BuilderSuffix = "Builder";

    public static bool TryFacadeNameFromBuilderInitializer(
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

    public static bool TryFacadeNameFromBuilderType(ExpressionSyntax builderType, out string facadeName)
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

    public static bool TryFindGeneratedFacade(
        SemanticModel model,
        ExpressionSyntax builderType,
        string facadeName,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        var builderSymbol = model.GetSymbolInfo(builderType, cancellationToken);
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
                if (found is not null && !SymbolEqualityComparer.Default.Equals(found, candidate))
                {
                    return false;
                }

                found = candidate;
            }
        }

        receiverType = found!;
        return found is not null;
    }

    public static bool TryTupleElementIndex(
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

    public static ExpressionSyntax? TupleElementInitializer(
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
}
