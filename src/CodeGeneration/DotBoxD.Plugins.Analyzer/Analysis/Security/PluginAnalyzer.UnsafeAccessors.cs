using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static bool ReportAndRecordIfUnsafeAccessor(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IMethodSymbol target)
    {
        if (!TryGetUnsafeAccessorAttribute(target, out var attribute))
        {
            return false;
        }

        helperGraph.RecordForbidden(target, attribute);
        if (!IsEventKernel(method.ContainingType))
        {
            return false;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            attribute.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        return true;
    }
}
