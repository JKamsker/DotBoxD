using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class LocalValueOrigins
{
    public static IEnumerable<ExpressionSyntax> GetValues(ILocalReferenceOperation local, Compilation compilation)
    {
        var values = new List<ExpressionSyntax>();
        foreach (var reference in local.Local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not VariableDeclaratorSyntax declaration)
            {
                continue;
            }

            if (declaration.Initializer is { Value: { } initializer })
            {
                values.Add(initializer);
            }

            if (declaration.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
            {
                continue;
            }

            var model = compilation.GetSemanticModel(block.SyntaxTree);
            foreach (var assignment in block.DescendantNodes(DescendInto).OfType<AssignmentExpressionSyntax>())
            {
                if (!IsRelevantAssignment(assignment, local, model))
                {
                    continue;
                }

                // Only a preceding straight-line statement replaces all earlier possibilities.
                // Branches and loop backedges add possible values without erasing the old ones.
                if (IsDefiniteWrite(assignment, local.Syntax))
                {
                    values.Clear();
                }

                values.Add(assignment.Right);
            }
        }

        return values;
    }

    private static bool IsRelevantAssignment(
        AssignmentExpressionSyntax assignment,
        ILocalReferenceOperation local,
        SemanticModel model)
        => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
           SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, local.Local) &&
           (assignment.SpanStart < local.Syntax.SpanStart || SharesLoop(assignment, local.Syntax));

    private static bool DescendInto(SyntaxNode node)
        => node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax;

    private static bool IsDefiniteWrite(AssignmentExpressionSyntax assignment, SyntaxNode read)
    {
        if (assignment.SpanStart >= read.SpanStart || assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return false;
        }

        // A captured local can be read long after its function was declared.
        if (ExecutableScope(assignment) != ExecutableScope(read))
        {
            return false;
        }

        for (SyntaxNode? current = statement.Parent; current is BlockSyntax block; current = block.Parent)
        {
            if (block.Span.Contains(read.Span))
            {
                return !block.DescendantNodes(DescendInto).OfType<GotoStatementSyntax>().Any();
            }
        }

        return false;
    }

    private static SyntaxNode? ExecutableScope(SyntaxNode node)
        => node.Ancestors().FirstOrDefault(ancestor => ancestor is AnonymousFunctionExpressionSyntax or
            LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax);

    private static bool SharesLoop(SyntaxNode assignment, SyntaxNode read)
        => read.Ancestors().Any(ancestor =>
            ancestor is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or
                WhileStatementSyntax or DoStatementSyntax && ancestor.Span.Contains(assignment.Span));
}
