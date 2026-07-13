using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeListPattern(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var pattern = (IListPatternOperation)context.Operation;
        RecordReachableMember(context, helperGraph, pattern.LengthSymbol);
        RecordReachableMember(context, helperGraph, pattern.IndexerSymbol);
    }

    private static void AnalyzeSlicePattern(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var pattern = (ISlicePatternOperation)context.Operation;
        RecordReachableMember(context, helperGraph, pattern.SliceSymbol);
    }

    private static void RecordReachableMember(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ISymbol? member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                RecordReachableMethod(context, helperGraph, method);
                break;
            case IPropertySymbol property:
                RecordReachableProperty(context, helperGraph, property);
                break;
        }
    }

    private static void RecordReachableMethod(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol target)
    {
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, target.ContainingType);
            RecordInitializerRootCall(context, helperGraph, target);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, target.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, target);
        ReportForbiddenReferencedMethodSignature(context, target);
        helperGraph.RecordCall(method, target, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, target);
    }

    private static void RecordReachableProperty(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IPropertySymbol property)
    {
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, property.ContainingType);
            RecordForbiddenInitializerReference(context, helperGraph, property.ContainingType);
            RecordForbiddenDelegateInitializer(context, helperGraph, property.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, property);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, property.ContainingType);
            RecordInitializerPropertyRootCall(context, helperGraph, property);
            RecordInitializerMemberReference(context, helperGraph, property);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, property.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, property);
        ReportForbiddenReferencedType(context, property.ContainingType, property.Type);
        ReportLocalUseIfInvalid(context, property);

        if (property.GetMethod is { } getter)
        {
            helperGraph.RecordCall(method, getter, context.Operation.Syntax.GetLocation());
        }
    }
}
