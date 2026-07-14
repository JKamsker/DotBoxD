using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string UnsafeCodeDisplayName = "unsafe pointer or stackalloc";

    private static void RegisterUnsafeCodeAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeUnsafeLocalDeclaration(c, helperGraph),
            SyntaxKind.LocalDeclarationStatement);

    private static void AnalyzeUnsafeLocalDeclaration(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not LocalDeclarationStatementSyntax declaration ||
            !ContainsUnsafeSyntax(declaration) ||
            context.SemanticModel.GetEnclosingSymbol(
                declaration.SpanStart,
                context.CancellationToken) is not IMethodSymbol method)
        {
            return;
        }

        helperGraph.RecordForbidden(method, UnsafeCodeDisplayName);
        if (!IsForbiddenApiRoot(context.SemanticModel.Compilation, method) ||
            !helperGraph.TryRecordDirectDiagnostic(method))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            declaration.GetLocation(),
            UnsafeCodeDisplayName));
    }

    private static bool ContainsUnsafeSyntax(LocalDeclarationStatementSyntax declaration)
        => declaration.DescendantNodesAndSelf().Any(static node =>
            node.IsKind(SyntaxKind.PointerType) ||
            node.IsKind(SyntaxKind.FunctionPointerType) ||
            node.IsKind(SyntaxKind.StackAllocArrayCreationExpression) ||
            node.IsKind(SyntaxKind.ImplicitStackAllocArrayCreationExpression));
}
