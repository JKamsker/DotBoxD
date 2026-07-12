using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeRecursivePattern(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (((IRecursivePatternOperation)context.Operation).DeconstructSymbol is not IMethodSymbol deconstruct)
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, deconstruct.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, deconstruct);
            RecordInitializerRootCall(context, helperGraph, deconstruct);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, deconstruct.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, deconstruct);
        ReportForbiddenReferencedMethodSignature(context, deconstruct);
        helperGraph.RecordCall(method, deconstruct, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, deconstruct);
    }
}
