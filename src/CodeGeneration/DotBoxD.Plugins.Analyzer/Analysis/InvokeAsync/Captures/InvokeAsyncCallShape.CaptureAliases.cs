using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncCallShape
{
    private static HashSet<ISymbol> CaptureBagAliases(
        BlockSyntax block,
        string captureParameterName,
        SemanticModel model)
    {
        var aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var declarator in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Initializer?.Value is { } initializer &&
                    IsCaptureBagExpression(initializer, captureParameterName, aliases, model) &&
                    model.GetDeclaredSymbol(declarator) is ILocalSymbol local)
                {
                    changed |= aliases.Add(local);
                }
            }

            foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                    assignment.Left is IdentifierNameSyntax left &&
                    IsCaptureBagExpression(assignment.Right, captureParameterName, aliases, model) &&
                    model.GetSymbolInfo(left).Symbol is ILocalSymbol local)
                {
                    changed |= aliases.Add(local);
                }
            }
        }

        ValidateCaptureBagAliasRebindings(block, captureParameterName, aliases, model);
        return aliases;
    }

    private static void ValidateCaptureBagAliasRebindings(
        BlockSyntax block,
        string captureParameterName,
        ISet<ISymbol> captureAliases,
        SemanticModel model)
    {
        foreach (var assignment in block.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                assignment.Left is IdentifierNameSyntax left &&
                model.GetSymbolInfo(left).Symbol is ILocalSymbol local &&
                captureAliases.Contains(local) &&
                !IsCaptureBagExpression(assignment.Right, captureParameterName, captureAliases, model))
            {
                throw new NotSupportedException(
                    $"InvokeAsync capture alias '{left.Identifier.ValueText}' cannot be reassigned away from the capture bag.");
            }
        }
    }

    private static bool IsCaptureBagExpression(
        ExpressionSyntax expression,
        string captureParameterName,
        ISet<ISymbol> captureAliases,
        SemanticModel model)
    {
        if (expression is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier).Symbol is not { } symbol)
        {
            return false;
        }

        return (symbol is IParameterSymbol parameter &&
                string.Equals(parameter.Name, captureParameterName, StringComparison.Ordinal)) ||
               captureAliases.Contains(symbol);
    }
}
