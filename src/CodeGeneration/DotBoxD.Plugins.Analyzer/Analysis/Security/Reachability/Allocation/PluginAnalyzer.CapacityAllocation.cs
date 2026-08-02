using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string StackOfTOriginalDefinition = "System.Collections.Generic.Stack<T>";

    private static void ReportAndRecordCapacityAllocationIfForbidden(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IObjectCreationOperation creation)
    {
        if (!TryGetForbiddenCapacityAllocationDisplayName(creation, out var forbidden))
        {
            return;
        }

        helperGraph.RecordForbidden(method, forbidden);
        if (!IsForbiddenApiRoot(context, method) ||
            !helperGraph.TryRecordDirectDiagnostic(method, forbidden))
        {
            return;
        }

        ReportCapacityAllocationDiagnostic(context, forbidden);
    }

    private static void ReportAndRecordCapacityAllocationInInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IObjectCreationOperation creation)
    {
        if (!TryGetForbiddenCapacityAllocationDisplayName(creation, out var forbidden))
        {
            return;
        }

        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
        {
            helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, forbidden);
        }

        if (context.ContainingSymbol is IPropertySymbol { GetMethod: { } getter } property &&
            !IsEventKernel(property.ContainingType))
        {
            helperGraph.RecordForbidden(getter, forbidden);
        }

        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol &&
            IsEventKernel(context.ContainingSymbol.ContainingType))
        {
            ReportCapacityAllocationDiagnostic(context, forbidden);
        }
    }

    private static bool TryGetForbiddenCapacityAllocationDisplayName(
        IObjectCreationOperation creation,
        out string forbidden)
    {
        if (creation.Type is not INamedTypeSymbol namedType ||
            creation.Constructor is not { Parameters.Length: 1 } constructor ||
            constructor.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
            !string.Equals(
                namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                StackOfTOriginalDefinition,
                StringComparison.Ordinal))
        {
            forbidden = null!;
            return false;
        }

        forbidden = namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return true;
    }

    private static void ReportCapacityAllocationDiagnostic(OperationAnalysisContext context, string forbidden)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            forbidden));
}
