using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void ReportForbiddenInvocationTypeArguments(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        IMethodSymbol targetMethod)
    {
        foreach (var typeArgument in targetMethod.TypeArguments)
        {
            if (FirstForbiddenHostApi(typeArgument) is not { } forbiddenType)
            {
                continue;
            }

            helperGraph.RecordForbidden(containingMethod, forbiddenType);
            if (IsForbiddenApiRoot(context, containingMethod))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ForbiddenHostApiRule,
                    context.Operation.Syntax.GetLocation(),
                    forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            }

            return;
        }
    }

    private static void RecordGenericNewConstraintConstructorReachability(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol containingMethod,
        IMethodSymbol targetMethod)
        => helperGraph.RecordGenericInvocation(
            containingMethod,
            targetMethod,
            context.Operation.Syntax.GetLocation());
}
