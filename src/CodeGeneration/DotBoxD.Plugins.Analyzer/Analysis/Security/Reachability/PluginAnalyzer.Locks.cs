using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string MonitorTypeName = "System.Threading.Monitor";

    private static void AnalyzeLock(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            return;
        }

        var monitorType = context.Compilation.GetTypeByMetadataName(MonitorTypeName);
        ReportAndRecordIfForbidden(context, helperGraph, method, monitorType);
    }
}
