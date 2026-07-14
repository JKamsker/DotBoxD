using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeImplicitIndexerReference(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var reference = (IImplicitIndexerReferenceOperation)context.Operation;
        RecordReachableMember(context, helperGraph, reference.LengthSymbol);
        RecordReachableMember(context, helperGraph, reference.IndexerSymbol);
    }
}
