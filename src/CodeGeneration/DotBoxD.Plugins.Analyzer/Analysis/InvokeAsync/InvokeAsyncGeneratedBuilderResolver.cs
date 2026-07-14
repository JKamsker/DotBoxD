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
                TryFacadeNameFromBuilderInitializer(initializer, out var facadeName, out var facadeMetadataName) &&
                TryFindGeneratedFacade(model.Compilation, facadeName, facadeMetadataName, cancellationToken, out receiverType))
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
            TupleElementInitializer(access.Expression, index, model, cancellationToken) is not { } initializer ||
            !TryFacadeNameFromBuilderInitializer(initializer, out var facadeName, out var facadeMetadataName))
        {
            return false;
        }

        return TryFindGeneratedFacade(model.Compilation, facadeName, facadeMetadataName, cancellationToken, out receiverType);
    }

    private static bool TryFacadeNameFromBuilderInitializer(
        ExpressionSyntax initializer,
        out string facadeName,
        out string? facadeMetadataName)
    {
        facadeName = string.Empty;
        facadeMetadataName = null;
        return initializer is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Build",
                Expression: { } buildReceiver
            }
        } && TryFacadeNameFromBuilderFactory(buildReceiver, out facadeName, out facadeMetadataName);
    }

    private static bool TryFacadeNameFromBuilderFactory(
        ExpressionSyntax buildReceiver,
        out string facadeName,
        out string? facadeMetadataName)
    {
        facadeName = string.Empty;
        facadeMetadataName = null;
        var current = buildReceiver;
        while (current is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: { } next }
            })
        {
            if (TryFacadeNameFromBuilderType(next, out facadeName, out facadeMetadataName))
            {
                return true;
            }

            current = next;
        }

        return TryFacadeNameFromBuilderType(current, out facadeName, out facadeMetadataName);
    }

    private static bool TryFacadeNameFromBuilderType(
        ExpressionSyntax builderType,
        out string facadeName,
        out string? facadeMetadataName)
    {
        var builderName = BuilderTypeName(builderType);
        if (!builderName.EndsWith(BuilderSuffix, StringComparison.Ordinal) ||
            builderName.Length == BuilderSuffix.Length)
        {
            facadeName = string.Empty;
            facadeMetadataName = null;
            return false;
        }

        facadeName = builderName.Substring(0, builderName.Length - BuilderSuffix.Length);
        var qualifier = BuilderTypeQualifier(builderType);
        facadeMetadataName = string.IsNullOrEmpty(qualifier) ? null : qualifier + "." + facadeName;
        return true;
    }

    private static string BuilderTypeName(ExpressionSyntax builderType)
        => builderType switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };

    private static string? BuilderTypeQualifier(ExpressionSyntax builderType)
        => builderType switch
        {
            QualifiedNameSyntax qualified => NameText(qualified.Left),
            MemberAccessExpressionSyntax member => ExpressionText(member.Expression),
            _ => null
        };

    private static string NameText(NameSyntax name)
        => name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => NameText(qualified.Left) + "." + qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global" } alias => alias.Name.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Alias.Identifier.ValueText + "." + alias.Name.Identifier.ValueText,
            _ => string.Empty
        };

    private static string? ExpressionText(ExpressionSyntax expression)
        => expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => NameText(qualified),
            MemberAccessExpressionSyntax member => ExpressionText(member.Expression) + "." + member.Name.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => NameText(alias),
            _ => null
        };

    private static bool TryFindGeneratedFacade(
        Compilation compilation,
        string facadeName,
        string? facadeMetadataName,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (facadeMetadataName is not null &&
            compilation.GetTypeByMetadataName(facadeMetadataName) is { } qualifiedCandidate &&
            InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(qualifiedCandidate))
        {
            receiverType = qualifiedCandidate;
            return true;
        }

        if (facadeMetadataName is not null)
        {
            return false;
        }

        foreach (var symbol in compilation.GetSymbolsWithName(
                     name => string.Equals(name, facadeName, StringComparison.Ordinal),
                     SymbolFilter.Type,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is INamedTypeSymbol candidate &&
                InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(candidate))
            {
                receiverType = candidate;
                return true;
            }
        }

        return false;
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
