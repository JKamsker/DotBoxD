using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDCapturedConstantLocal
{
    public static DotBoxDExpressionModel? TryLower(
        IdentifierNameSyntax identifier,
        DotBoxDExpressionLoweringContext context)
    {
        if (!TryInitializer(identifier, context.SemanticModel, context.CancellationToken, out var initializer))
        {
            return null;
        }

        return DotBoxDConstantExpressionLowerer.TryLower(
            initializer,
            context.SemanticModel,
            context.CancellationToken);
    }

    public static bool TryGetConstantValue(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        out object? value)
    {
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue)
        {
            value = constant.Value;
            return true;
        }

        if (expression is IdentifierNameSyntax identifier &&
            TryInitializer(identifier, model, cancellationToken, out var initializer))
        {
            constant = model.GetConstantValue(initializer, cancellationToken);
            if (constant.HasValue)
            {
                value = constant.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryInitializer(
        IdentifierNameSyntax identifier,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializer)
    {
        initializer = null!;
        if (model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } value
                } declarator &&
                declarator.SpanStart < identifier.SpanStart &&
                IsEffectivelyFinal(local, declarator, model, cancellationToken))
            {
                initializer = value;
                return true;
            }
        }

        return false;
    }

    private static bool IsEffectivelyFinal(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarator,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var root = (SyntaxNode?)declarator.FirstAncestorOrSelf<BlockSyntax>() ??
            declarator.SyntaxTree.GetRoot(cancellationToken);
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.SpanStart <= declarator.SpanStart)
            {
                continue;
            }

            if (IsMutationOfLocal(node, local, model, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMutationOfLocal(
        SyntaxNode node,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
        => node switch
        {
            AssignmentExpressionSyntax assignment =>
                ExpressionNamesLocal(assignment.Left, local, model, cancellationToken),
            ArgumentSyntax argument when IsWritableByRef(argument) =>
                ExpressionNamesLocal(argument.Expression, local, model, cancellationToken),
            PrefixUnaryExpressionSyntax prefix =>
                IsPrefixMutationOfLocal(prefix, local, model, cancellationToken),
            PostfixUnaryExpressionSyntax postfix =>
                IsPostfixMutationOfLocal(postfix, local, model, cancellationToken),
            _ => false
        };

    private static bool IsPrefixMutationOfLocal(
        PrefixUnaryExpressionSyntax prefix,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!prefix.IsKind(SyntaxKind.PreIncrementExpression) &&
            !prefix.IsKind(SyntaxKind.PreDecrementExpression))
        {
            return false;
        }

        return ExpressionNamesLocal(prefix.Operand, local, model, cancellationToken);
    }

    private static bool IsPostfixMutationOfLocal(
        PostfixUnaryExpressionSyntax postfix,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!postfix.IsKind(SyntaxKind.PostIncrementExpression) &&
            !postfix.IsKind(SyntaxKind.PostDecrementExpression))
        {
            return false;
        }

        return ExpressionNamesLocal(postfix.Operand, local, model, cancellationToken);
    }

    private static bool IsWritableByRef(ArgumentSyntax argument)
        => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
           argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);

    private static bool ExpressionNamesLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = Unwrap(expression);
        if (expression is IdentifierNameSyntax identifier &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
        {
            return true;
        }

        if (expression is TupleExpressionSyntax tuple)
        {
            foreach (var argument in tuple.Arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ExpressionNamesLocal(argument.Expression, local, model, cancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current;
    }
}
