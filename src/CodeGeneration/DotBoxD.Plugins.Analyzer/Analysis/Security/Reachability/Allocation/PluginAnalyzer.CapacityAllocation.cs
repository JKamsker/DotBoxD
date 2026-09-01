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
        if (!ForbiddenCollectionCapacityPolicy.TryGetDisplayName(creation, out var forbidden))
        {
            return;
        }

        ReportAndRecordCollectionCapacityOperation(context, helperGraph, forbidden);
    }

    private static void ReportAndRecordCollectionCapacityInvocation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IInvocationOperation invocation)
        => ReportAndRecordCollectionCapacityAllocation(context, helperGraph, invocation.TargetMethod);

    private static void ReportAndRecordCollectionScanInvocation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IInvocationOperation invocation)
    {
        if (ForbiddenCollectionScanPolicy.TryGetDisplayName(invocation.TargetMethod, out var forbidden))
        {
            ReportAndRecordCollectionCapacityOperation(context, helperGraph, forbidden);
        }
    }

    private static void ReportAndRecordCollectionCapacityAllocation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol? allocationMethod)
    {
        if (!ForbiddenCollectionCapacityPolicy.TryGetDisplayName(allocationMethod, out var forbidden))
        {
            return;
        }

        ReportAndRecordCollectionCapacityOperation(context, helperGraph, forbidden);
    }

    private static void ReportAndRecordCollectionCapacitySetter(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IPropertySymbol property,
        bool usesSetter)
    {
        if (!usesSetter ||
            !ForbiddenCollectionCapacityPolicy.TryGetDisplayName(property, out var forbidden))
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
