using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeCollectionExpression(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var constructMethod = ((ICollectionExpressionOperation)context.Operation).ConstructMethod;
        if (constructMethod is null)
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, constructMethod.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, constructMethod);
            RecordInitializerRootCall(context, helperGraph, constructMethod);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, constructMethod.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, constructMethod);
        helperGraph.RecordCall(method, constructMethod, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, constructMethod);
    }
}
