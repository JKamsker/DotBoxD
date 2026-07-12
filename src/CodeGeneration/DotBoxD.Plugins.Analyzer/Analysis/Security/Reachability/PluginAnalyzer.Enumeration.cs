using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterEnumerationSyntaxAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        context.RegisterSyntaxNodeAction(
            c => AnalyzeForEachStatement(c, helperGraph),
            SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement);
    }

    private static void AnalyzeForEachStatement(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not CommonForEachStatementSyntax foreachSyntax ||
            context.SemanticModel.GetEnclosingSymbol(
                foreachSyntax.SpanStart,
                context.CancellationToken) is not IMethodSymbol method)
        {
            return;
        }

        var info = context.SemanticModel.GetForEachStatementInfo(foreachSyntax);
        var location = foreachSyntax.GetLocation();
        RecordImplicitEnumeratorMethod(context, helperGraph, method, info.GetEnumeratorMethod, location);
        RecordImplicitEnumeratorMethod(context, helperGraph, method, info.MoveNextMethod, location);
        RecordImplicitEnumeratorMethod(context, helperGraph, method, info.DisposeMethod, location);

        if (info.CurrentProperty?.GetMethod is { } getter)
        {
            RecordImplicitEnumeratorMethod(context, helperGraph, method, getter, location);
        }
    }

    private static void RecordImplicitEnumeratorMethod(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol? target,
        Location location)
    {
        if (target is null)
        {
            return;
        }

        ReportAndRecordImplicitEnumeratorIfForbidden(context, helperGraph, method, target, location);
        RecordImplicitEnumeratorStaticConstructor(helperGraph, method, target, location);
        helperGraph.RecordCall(method, target, location);
    }

    private static void ReportAndRecordImplicitEnumeratorIfForbidden(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol target,
        Location location)
    {
        if (!IsForbiddenHostApi(target.ContainingType))
        {
            return;
        }

        helperGraph.RecordForbidden(method, target.ContainingType);
        if (!IsEventKernel(method.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            location,
            target.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void RecordImplicitEnumeratorStaticConstructor(
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol target,
        Location location)
    {
        if (StaticConstructor(target.ContainingType) is { } staticConstructor)
        {
            helperGraph.RecordCall(method, staticConstructor, location);
        }
    }
}
