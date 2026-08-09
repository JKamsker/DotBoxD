using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void ReportAndRecordCollectionCapacityCreation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IObjectCreationOperation creation)
    {
        if (!ForbiddenCollectionCapacityPolicy.TryGetDisplayName(creation.Constructor, out var forbidden))
        {
            return;
        }

        ReportAndRecordCollectionCapacityOperation(context, helperGraph, forbidden);
    }

    private static void ReportAndRecordCollectionCapacityInvocation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IInvocationOperation invocation)
    {
        if (!ForbiddenCollectionCapacityPolicy.TryGetDisplayName(invocation.TargetMethod, out var forbidden))
        {
            return;
        }

        ReportAndRecordCollectionCapacityOperation(context, helperGraph, forbidden);
    }

    private static void ReportAndRecordCollectionCapacityOperation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        string forbidden)
    {
        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordForbidden(method, forbidden);
            if (IsForbiddenApiRoot(context, method) &&
                helperGraph.TryRecordDirectDiagnostic(method, forbidden))
            {
                ReportCollectionCapacityDiagnostic(context, forbidden);
            }

            return;
        }

        if (context.ContainingSymbol is not IFieldSymbol and not IPropertySymbol)
        {
            return;
        }

        var initializer = context.ContainingSymbol;
        helperGraph.RecordForbiddenInitializer(initializer, forbidden);
        var isEventKernel = IsEventKernel(initializer.ContainingType);
        if (isEventKernel)
        {
            ReportCollectionCapacityDiagnostic(context, forbidden);
        }

        if (!isEventKernel && initializer is IPropertySymbol { GetMethod: { } getter })
        {
            helperGraph.RecordForbidden(getter, forbidden);
        }
    }

    private static void ReportCollectionCapacityDiagnostic(OperationAnalysisContext context, string forbidden)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            forbidden));
}
