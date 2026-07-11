using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Architecture.Tests;

internal sealed class CyclomaticComplexityWalker(SyntaxNode root) : CSharpSyntaxWalker
{
    private readonly SyntaxNode root = root;

    public int DecisionPoints { get; private set; }

    public static int Calculate(IEnumerable<SyntaxNode> nodes)
    {
        var complexity = 1;
        foreach (var node in nodes)
        {
            var walker = new CyclomaticComplexityWalker(node);
            walker.Visit(node);
            complexity += walker.DecisionPoints;
        }

        return complexity;
    }

    public override void Visit(SyntaxNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (!ReferenceEquals(node, root) && IsNestedExecutableBoundary(node))
        {
            return;
        }

        base.Visit(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitIfStatement(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitForStatement(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitForEachStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitWhileStatement(node);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitDoStatement(node);
    }

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        DecisionPoints++;
        base.VisitCaseSwitchLabel(node);
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
    {
        DecisionPoints++;
        base.VisitDefaultSwitchLabel(node);
    }

    public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        DecisionPoints++;
        base.VisitSwitchExpressionArm(node);
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        DecisionPoints++;
        base.VisitCatchClause(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        DecisionPoints++;
        base.VisitConditionalExpression(node);
    }

    public override void VisitWhenClause(WhenClauseSyntax node)
    {
        DecisionPoints++;
        base.VisitWhenClause(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
        {
            DecisionPoints++;
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        if (node.IsKind(SyntaxKind.AndPattern) || node.IsKind(SyntaxKind.OrPattern))
        {
            DecisionPoints++;
        }

        base.VisitBinaryPattern(node);
    }

    private static bool IsNestedExecutableBoundary(SyntaxNode node)
        => node is BaseMethodDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or AnonymousFunctionExpressionSyntax
            || node is PropertyDeclarationSyntax { ExpressionBody: not null }
            || node is IndexerDeclarationSyntax { ExpressionBody: not null };
}
