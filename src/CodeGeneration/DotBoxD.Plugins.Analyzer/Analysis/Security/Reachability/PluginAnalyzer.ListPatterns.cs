using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeListPattern(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var pattern = (IListPatternOperation)context.Operation;
        RecordPatternMember(context, helperGraph, pattern.LengthSymbol);
        RecordPatternMember(context, helperGraph, pattern.IndexerSymbol);
    }

    private static void AnalyzeSlicePattern(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var pattern = (ISlicePatternOperation)context.Operation;
        RecordPatternMember(context, helperGraph, pattern.SliceSymbol);
    }

    private static void RecordPatternMember(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ISymbol? member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                RecordPatternMethod(context, helperGraph, method);
                break;
            case IPropertySymbol property:
                RecordPatternProperty(context, helperGraph, property);
                break;
        }
    }

    private static void RecordPatternMethod(
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
        ReportForbiddenReferencedMethodSignature(context, helperGraph, target);
        helperGraph.RecordCall(method, target, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, target);
    }

    private static void RecordPatternProperty(
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
        ReportForbiddenReferencedType(context, helperGraph, property.ContainingType, property.Type);
        ReportLocalUseIfInvalid(context, property);

        if (property.GetMethod is { } getter)
        {
            helperGraph.RecordCall(method, getter, context.Operation.Syntax.GetLocation());
        }
    }
}
