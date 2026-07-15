using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string ArrayAllocationForbiddenApiName = "array allocation";

    private static void AnalyzeArrayCreation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Operation.IsImplicit)
        {
            return;
        }

        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordForbidden(method, ArrayAllocationForbiddenApiName);
            if (IsForbiddenApiRoot(context, method) &&
                helperGraph.TryRecordDirectDiagnostic(method, ArrayAllocationForbiddenApiName))
            {
                ReportArrayAllocationDiagnostic(context);
            }

            return;
        }

        ReportArrayAllocationInInitializer(context);
        RecordArrayAllocationInitializerReference(context, helperGraph);
        RecordArrayAllocationHelperPropertyInitializer(context, helperGraph);
    }

    private static void ReportArrayAllocationInInitializer(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol) ||
            !IsEventKernel(context.ContainingSymbol.ContainingType))
        {
            return;
        }

        ReportArrayAllocationDiagnostic(context);
    }

    private static void RecordArrayAllocationInitializerReference(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
        {
            helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, ArrayAllocationForbiddenApiName);
        }
    }

    private static void RecordArrayAllocationHelperPropertyInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is IPropertySymbol { GetMethod: { } getter } property &&
            !IsEventKernel(property.ContainingType))
        {
            helperGraph.RecordForbidden(getter, ArrayAllocationForbiddenApiName);
        }
    }

    private static void ReportArrayAllocationDiagnostic(OperationAnalysisContext context)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            ArrayAllocationForbiddenApiName));
}
