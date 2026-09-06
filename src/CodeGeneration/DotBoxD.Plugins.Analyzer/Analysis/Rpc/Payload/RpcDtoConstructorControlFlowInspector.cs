using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorControlFlowInspector
{
    public static bool CanSkipAssignment(
        ConstructorDeclarationSyntax declaration,
        AssignmentExpressionSyntax assignment)
    {
        var assignmentStart = assignment.SpanStart;
        var statements = declaration.DescendantNodes(static node =>
                node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<StatementSyntax>();
        var labels = statements.OfType<LabeledStatementSyntax>().ToArray();

        foreach (var statement in statements)
        {
            if (statement.SpanStart >= assignmentStart)
            {
                continue;
            }

            if (statement is ReturnStatementSyntax or GotoStatementSyntax { CaseOrDefaultKeyword.RawKind: not 0 })
            {
                return true;
            }

            if (statement is GotoStatementSyntax { Expression: IdentifierNameSyntax target } &&
                labels.Any(label =>
                    label.Identifier.ValueText == target.Identifier.ValueText &&
                    label.SpanStart > assignmentStart))
            {
                return true;
            }
        }

        return false;
    }
}
