using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static GeneratedRemoteHookChainTarget? TargetFromLocalAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (expression is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return null;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(cancellationToken))
            {
                case VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                }:
                    return RegistryTarget(initializer, model, cancellationToken, depth + 1);
                case SingleVariableDesignationSyntax designation
                    when DeconstructionInitializer(designation, cancellationToken) is { } initializer:
                    return RegistryTarget(initializer, model, cancellationToken, depth + 1);
            }
        }

        return null;
    }

    private static ExpressionSyntax? DeconstructionInitializer(
        SingleVariableDesignationSyntax designation,
        CancellationToken cancellationToken)
    {
        if (designation.Parent is not ParenthesizedVariableDesignationSyntax variables ||
            variables.Parent is not DeclarationExpressionSyntax declaration ||
            declaration.Parent is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var index = variables.Variables.IndexOf(designation);
        var right = HookChainAliasResolver.UnwrapTransparentExpression(assignment.Right);
        return right is TupleExpressionSyntax tuple &&
            index >= 0 &&
            index < tuple.Arguments.Count
            ? tuple.Arguments[index].Expression
            : null;
    }
}
