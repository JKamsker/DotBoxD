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
        => TryResolve(model, receiver, cancellationToken, depth: 0, out receiverType);

    private static bool TryResolve(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        int depth,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (depth > 8)
        {
            return false;
        }

        receiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);
        if (TryResolveGeneratedBuilderCall(model, receiver, cancellationToken, out receiverType) ||
            TryResolveGeneratedBuilderComposition(model, receiver, cancellationToken, depth, out receiverType) ||
            TryResolveGeneratedBuilderProjection(model, receiver, cancellationToken, out receiverType) ||
            TryResolveGeneratedFacadeExpression(model, receiver, cancellationToken, out receiverType))
        {
            return true;
        }

        return TryResolveGeneratedBuilderLocal(model, receiver, cancellationToken, depth, out receiverType);
    }

    private static bool TryResolveGeneratedBuilderComposition(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        int depth,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (receiver is ConditionalExpressionSyntax conditional)
        {
            return TryResolveMatchingTypes(
                model, conditional.WhenTrue, conditional.WhenFalse, cancellationToken, depth, out receiverType);
        }

        return receiver is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.CoalesceExpression) &&
            TryResolveMatchingTypes(model, binary.Left, binary.Right, cancellationToken, depth, out receiverType);
    }

    private static bool TryResolveMatchingTypes(
        SemanticModel model,
        ExpressionSyntax left,
        ExpressionSyntax right,
        CancellationToken cancellationToken,
        int depth,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        if (!TryResolve(model, left, cancellationToken, depth + 1, out var leftType) ||
            !TryResolve(model, right, cancellationToken, depth + 1, out var rightType) ||
            !SymbolEqualityComparer.Default.Equals(leftType, rightType))
        {
            return false;
        }

        receiverType = leftType;
        return true;
    }

    private static bool TryResolveGeneratedBuilderCall(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        return TryFacadeNameFromBuilderCall(receiver, out var facadeName) &&
            TryFindGeneratedFacade(model.Compilation, facadeName, cancellationToken, out receiverType);
    }

    private static bool TryResolveGeneratedBuilderProjection(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        return receiver is MemberAccessExpressionSyntax access &&
            TryTupleElementIndex(access, model, cancellationToken, out var index) &&
            TupleElementInitializer(access.Expression, index, model, cancellationToken) is { } initializer &&
            TryResolve(model, initializer, cancellationToken, depth: 0, out receiverType);
    }

    private static bool TryResolveGeneratedFacadeExpression(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
        var semanticType = model.GetTypeInfo(receiver, cancellationToken).Type as INamedTypeSymbol;
        receiverType = semanticType!;
        return semanticType is not null &&
            InvokeAsyncAttributeMatcher.HasGeneratePluginServerAttribute(semanticType);
    }

    private static bool TryResolveGeneratedBuilderLocal(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        int depth,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
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
                TryResolve(model, initializer, cancellationToken, depth + 1, out receiverType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFacadeNameFromBuilderCall(
        ExpressionSyntax initializer,
        out string facadeName)
    {
        facadeName = string.Empty;
        return initializer is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Build",
                Expression: { } buildReceiver
            }
        } && TryFacadeNameFromBuilderFactory(buildReceiver, out facadeName);
    }

    private static bool TryFacadeNameFromBuilderFactory(
        ExpressionSyntax buildReceiver,
        out string facadeName)
    {
        facadeName = string.Empty;
        var current = buildReceiver;
        while (current is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: { } next }
            })
        {
            if (TryFacadeNameFromBuilderType(next, out facadeName))
            {
                return true;
            }

            current = next;
        }

        return TryFacadeNameFromBuilderType(current, out facadeName);
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
        Compilation compilation,
        string facadeName,
        CancellationToken cancellationToken,
        out INamedTypeSymbol receiverType)
    {
        receiverType = null!;
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
