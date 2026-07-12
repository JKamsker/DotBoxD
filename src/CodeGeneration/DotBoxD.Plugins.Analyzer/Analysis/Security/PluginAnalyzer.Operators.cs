using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeOperator(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var operatorMethod = context.Operation switch
        {
            IUnaryOperation unary => unary.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            _ => null
        };

        if (operatorMethod is null)
        {
            return;
        }

        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, operatorMethod.ContainingType);
            RecordInitializerRootCall(context, helperGraph, operatorMethod);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, operatorMethod.ContainingType);
        helperGraph.RecordCall(method, operatorMethod, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, operatorMethod);
    }
}
