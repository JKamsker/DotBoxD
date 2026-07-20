using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void RegisterAwaitReachabilityAnalysis(
        CompilationStartAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
        => context.RegisterSyntaxNodeAction(
            c => AnalyzeAwait(c, helperGraph),
            SyntaxKind.AwaitExpression);

    private static void AnalyzeAwait(SyntaxNodeAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Node is not AwaitExpressionSyntax awaitExpression ||
            context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        var awaitInfo = context.SemanticModel.GetAwaitExpressionInfo(awaitExpression);
        var location = awaitExpression.GetLocation();
        RecordAwaiterCall(context, helperGraph, method, awaitInfo.GetAwaiterMethod, location);
        if (awaitInfo.IsCompletedProperty?.GetMethod is { } isCompletedGetter)
        {
            RecordAwaiterCall(context, helperGraph, method, isCompletedGetter, location);
        }

        RecordAwaiterCall(context, helperGraph, method, awaitInfo.GetResultMethod, location);
    }

    private static void RecordAwaiterCall(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol? awaiterMethod,
        Location location)
    {
        if (awaiterMethod is null)
        {
            return;
        }

        ReportAndRecordForbiddenAwaiter(context, helperGraph, method, awaiterMethod.ContainingType, location);
        helperGraph.RecordCall(method, awaiterMethod, location);
        ReportAndRecordForbiddenAwaiterResult(context, helperGraph, method, awaiterMethod, location);
        ReportLocalAwaiterUseIfInvalid(context, awaiterMethod, location);
    }

    private static void RecordAwaiterCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol? awaiterMethod,
        Location location)
    {
        if (awaiterMethod is null)
        {
            return;
        }

        ReportAndRecordForbiddenAwaiter(context, helperGraph, method, awaiterMethod.ContainingType, location);
        helperGraph.RecordCall(method, awaiterMethod, location);
        ReportAndRecordForbiddenAwaiterResult(context, helperGraph, method, awaiterMethod, location);
        ReportLocalUseIfInvalid(context, awaiterMethod);
    }

    private static void ReportAndRecordForbiddenAwaiter(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? type,
        Location location)
    {
        if (!IsForbiddenHostApi(type))
        {
            return;
        }

        RecordAndReportForbiddenAwaiter(context, helperGraph, method, type!, location);
    }

    private static void ReportAndRecordForbiddenAwaiter(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? type,
        Location location)
    {
        if (!IsForbiddenHostApi(type))
        {
            return;
        }

        RecordAndReportForbiddenAwaiter(context, helperGraph, method, type!, location);
    }

    private static void ReportAndRecordForbiddenAwaiterResult(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        IMethodSymbol awaiterMethod,
        Location location)
    {
        if (!TryGetForbiddenHostApi(awaiterMethod.ReturnType, out var forbiddenType))
        {
            return;
        }

        RecordAndReportForbiddenAwaiter(context, helperGraph, containingMethod, forbiddenType, location);
    }

    private static void ReportAndRecordForbiddenAwaiterResult(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        IMethodSymbol awaiterMethod,
        Location location)
    {
        if (!TryGetForbiddenHostApi(awaiterMethod.ReturnType, out var forbiddenType))
        {
            return;
        }

        RecordAndReportForbiddenAwaiter(context, helperGraph, containingMethod, forbiddenType, location);
    }

    private static void RecordAndReportForbiddenAwaiter(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        ITypeSymbol forbiddenType,
        Location location)
    {
        helperGraph.RecordForbidden(containingMethod, forbiddenType);
        if (!IsEventKernel(containingMethod.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            location,
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void RecordAndReportForbiddenAwaiter(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        ITypeSymbol forbiddenType,
        Location location)
    {
        helperGraph.RecordForbidden(containingMethod, forbiddenType);
        if (!IsEventKernel(containingMethod.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            location,
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void ReportLocalAwaiterUseIfInvalid(
        SyntaxNodeAnalysisContext context,
        ISymbol target,
        Location location)
    {
        if (!HasAttribute(target, DotBoxDMetadataNames.NativeOnlyAttribute) ||
            !IsLocalUseForbidden(context.Node, context.ContainingSymbol, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PluginAnalyzerDiagnostics.LocalContextMemberRule,
            location,
            "[NativeOnly] context members run natively and cannot be used in lowered hook chains or server-extension bodies."));
    }

    private static void RecordAwaitablePatternCalls(
        SyntaxNodeAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? awaitableType,
        Location location)
    {
        if (awaitableType is null || awaitableType.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        foreach (var member in awaitableType.GetMembers("GetAwaiter").OfType<IMethodSymbol>())
        {
            if (member.Parameters.Length != 0 || member.IsStatic)
            {
                continue;
            }

            RecordAwaiterCall(context, helperGraph, method, member, location);
            if (member.ReturnType.GetMembers("IsCompleted").OfType<IPropertySymbol>().FirstOrDefault()?.GetMethod is
                { } isCompletedGetter)
            {
                RecordAwaiterCall(context, helperGraph, method, isCompletedGetter, location);
            }

            var getResult = member.ReturnType
                .GetMembers("GetResult")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static m => m.Parameters.Length == 0);
            RecordAwaiterCall(context, helperGraph, method, getResult, location);
            return;
        }
    }

    private static void RecordAwaitablePatternCalls(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? awaitableType,
        Location location)
    {
        if (awaitableType is null || awaitableType.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        foreach (var member in awaitableType.GetMembers("GetAwaiter").OfType<IMethodSymbol>())
        {
            if (member.Parameters.Length != 0 || member.IsStatic)
            {
                continue;
            }

            RecordAwaiterCall(context, helperGraph, method, member, location);
            if (member.ReturnType.GetMembers("IsCompleted").OfType<IPropertySymbol>().FirstOrDefault()?.GetMethod is
                { } isCompletedGetter)
            {
                RecordAwaiterCall(context, helperGraph, method, isCompletedGetter, location);
            }

            var getResult = member.ReturnType
                .GetMembers("GetResult")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static m => m.Parameters.Length == 0);
            RecordAwaiterCall(context, helperGraph, method, getResult, location);
            return;
        }
    }
}
