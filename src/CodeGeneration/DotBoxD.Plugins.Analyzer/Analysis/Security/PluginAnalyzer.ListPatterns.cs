using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeListPattern(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var pattern = (IListPatternOperation)context.Operation;
        RecordListPatternPropertyAccess(context, helperGraph, pattern.LengthSymbol);
        RecordListPatternPropertyAccess(context, helperGraph, pattern.IndexerSymbol);
    }

    private static void RecordListPatternPropertyAccess(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ISymbol? symbol)
    {
        if (symbol is not IPropertySymbol property)
        {
            return;
        }

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
