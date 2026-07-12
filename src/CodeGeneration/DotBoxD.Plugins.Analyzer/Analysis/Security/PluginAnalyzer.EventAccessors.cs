using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeEventReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var eventSymbol = ((IEventReferenceOperation)context.Operation).Event;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            RecordInitializerEventRootCall(context, helperGraph, eventSymbol);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, eventSymbol.ContainingType);

        var (usesAdd, usesRemove) = EventAccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesAdd && eventSymbol.AddMethod is { } addMethod)
        {
            helperGraph.RecordCall(method, addMethod, location);
        }

        if (usesRemove && eventSymbol.RemoveMethod is { } removeMethod)
        {
            helperGraph.RecordCall(method, removeMethod, location);
        }
    }

    private static void RecordInitializerEventRootCall(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IEventSymbol eventSymbol)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        var containingType = context.ContainingSymbol.ContainingType;
        var (usesAdd, usesRemove) = EventAccessorUsage(context.Operation);
        var location = context.Operation.Syntax.GetLocation();
        if (usesAdd && eventSymbol.AddMethod is { } addMethod)
        {
            helperGraph.RecordInitializerRootCall(containingType, addMethod, location);
        }

        if (usesRemove && eventSymbol.RemoveMethod is { } removeMethod)
        {
            helperGraph.RecordInitializerRootCall(containingType, removeMethod, location);
        }
    }

    private static (bool Add, bool Remove) EventAccessorUsage(IOperation reference)
    {
        if (reference.Parent is IEventAssignmentOperation assignment &&
            ReferenceEquals(assignment.EventReference, reference))
        {
            return assignment.Adds ? (true, false) : (false, true);
        }

        return (true, true);
    }
}
