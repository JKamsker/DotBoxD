using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterForbiddenTypeSyntaxAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        context.RegisterSyntaxNodeAction(
            c => AnalyzeDeclarationPatternType(c, helperGraph),
            SyntaxKind.DeclarationPattern);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeTypePatternType(c, helperGraph),
            SyntaxKind.TypePattern);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeRecursivePatternType(c, helperGraph),
            SyntaxKind.RecursivePattern);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeIsExpressionType(c, helperGraph),
            SyntaxKind.IsExpression);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeCatchDeclarationType(c, helperGraph),
            SyntaxKind.CatchDeclaration);
        context.RegisterSyntaxNodeAction(
            c => AnalyzeConstantPatternType(c, helperGraph),
            SyntaxKind.ConstantPattern);
    }

    private static void AnalyzeDeclarationPatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is DeclarationPatternSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeTypePatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is TypePatternSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeRecursivePatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is RecursivePatternSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeIsExpressionType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is BinaryExpressionSyntax { Right: TypeSyntax typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeCatchDeclarationType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is CatchDeclarationSyntax { Type: { } typeSyntax })
        {
            AnalyzeForbiddenTypeSyntax(context, helperGraph, typeSyntax);
        }
    }

    private static void AnalyzeConstantPatternType(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not ConstantPatternSyntax { Expression: { } expression })
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol is ITypeSymbol type)
        {
            AnalyzeForbiddenTypeSymbol(context, helperGraph, expression, type);
        }
    }

    private static void AnalyzeForbiddenTypeSyntax(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        TypeSyntax typeSyntax)
    {
        var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
        AnalyzeForbiddenTypeSymbol(context, helperGraph, typeSyntax, type);
    }

    private static void AnalyzeForbiddenTypeSymbol(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        SyntaxNode locationNode,
        ITypeSymbol? type)
    {
        if (!IsForbiddenHostApi(type))
        {
            return;
        }

        if (context.SemanticModel.GetEnclosingSymbol(
            context.Node.SpanStart,
            context.CancellationToken) is not IMethodSymbol method)
        {
            return;
        }

        helperGraph.RecordForbidden(method, type!);
        if (!IsEventKernel(method.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            locationNode.GetLocation(),
            type!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }
}
