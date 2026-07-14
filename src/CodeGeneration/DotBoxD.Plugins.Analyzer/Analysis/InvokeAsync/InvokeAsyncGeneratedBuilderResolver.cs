using System.Globalization;
using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
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
        receiverType = null!;
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
                TryResolveFacadeFromBuilderInitializer(
                    model,
                    initializer,
                    cancellationToken,
                    out receiverType))
            {
                return true;
            }
        }

        return false;
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

        return TryResolveFacadeFromBuilderInitializer(model, initializer, cancellationToken, out receiverType);
    }

    private static bool TryResolveFacadeFromBuilderInitializer(
        SemanticModel model,
        ExpressionSyntax initializer,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        return initializer is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Build",
                Expression: { } buildReceiver
            }
        } && TryResolveFacadeFromBuilderFactory(
            model,
            buildReceiver,
            cancellationToken,
            out receiverType);
    }

    private static bool TryResolveFacadeFromBuilderFactory(
        SemanticModel model,
        ExpressionSyntax buildReceiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        var current = buildReceiver;
        while (current is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: { } next }
            })
        {
            if (TryResolveFacadeFromBuilderType(
                    model,
                    next,
                    cancellationToken,
                    out receiverType))
            {
                return true;
            }

            current = next;
        }

        return TryResolveFacadeFromBuilderType(
            model,
            current,
            cancellationToken,
            out receiverType);
    }

    private static bool TryResolveFacadeFromBuilderType(
        SemanticModel model,
        ExpressionSyntax builderType,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        return TryFacadeNameFromBuilderType(builderType, out var facadeName) &&
               TryFindGeneratedFacade(
                   model,
                   builderType,
                   facadeName,
                   cancellationToken,
                   out receiverType);
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
        var qualifier = BuilderQualifier(builderType);
        if (qualifier is null)
        {
            return TrySelectGeneratedFacade(
                model.LookupNamespacesAndTypes(builderType.SpanStart, name: facadeName)
                    .OfType<INamedTypeSymbol>(),
                cancellationToken,
                out receiverType);
        }

        var container = QualifierSymbol(model, qualifier, cancellationToken);
        var candidates = container switch
        {
            INamespaceSymbol @namespace => @namespace.GetTypeMembers(facadeName),
            INamedTypeSymbol type => type.GetTypeMembers(facadeName),
            _ => []
        };
        return TrySelectGeneratedFacade(candidates, cancellationToken, out receiverType);
    }

    private static SyntaxNode? BuilderQualifier(ExpressionSyntax builderType)
        => builderType switch
        {
            QualifiedNameSyntax qualified => qualified.Left,
            MemberAccessExpressionSyntax member => member.Expression,
            _ => null
        };

    private static ISymbol? QualifierSymbol(
        SemanticModel model,
        SyntaxNode qualifier,
        CancellationToken cancellationToken)
    {
        if (qualifier is IdentifierNameSyntax identifier &&
            model.GetAliasInfo(identifier, cancellationToken) is { } alias)
        {
            return alias.Target;
        }

        return model.GetSymbolInfo(qualifier, cancellationToken).Symbol;
    }

    private static bool TrySelectGeneratedFacade(
        IEnumerable<INamedTypeSymbol> candidates,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(candidate))
            {
                continue;
            }

            if (receiverType is not null &&
                !SymbolEqualityComparer.Default.Equals(receiverType, candidate))
            {
                receiverType = null!;
                return false;
            }

            receiverType = candidate;
        }

        return receiverType is not null;
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
}
