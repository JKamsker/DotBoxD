using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string ListCapacityForbiddenApiName = "System.Collections.Generic.List";

    private static bool ReportAndRecordListCapacityCreation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IObjectCreationOperation creation)
    {
        if (!IsListCapacityConstructor(creation.Constructor))
        {
            return false;
        }

        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordForbidden(method, ListCapacityForbiddenApiName);
            if (IsForbiddenApiRoot(context, method) &&
                helperGraph.TryRecordDirectDiagnostic(method, ListCapacityForbiddenApiName))
            {
                ReportListCapacityDiagnostic(context);
            }

            return true;
        }

        ReportListCapacityInInitializer(context);
        RecordListCapacityInitializerReference(context, helperGraph);
        RecordListCapacityHelperPropertyInitializer(context, helperGraph);
        return true;
    }

    private static bool IsListCapacityConstructor(IMethodSymbol? constructor)
    {
        if (constructor is not
            {
                MethodKind: MethodKind.Constructor,
                Parameters.Length: 1,
                ContainingType: { } containingType
            })
        {
            return false;
        }

        var parameter = constructor.Parameters[0];
        return parameter.Name == "capacity" &&
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(
                containingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                DotBoxDGenerationNames.TypeNames.ListOriginal,
                StringComparison.Ordinal);
    }

    private static void ReportListCapacityInInitializer(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not (IFieldSymbol or IPropertySymbol) ||
            !IsEventKernel(context.ContainingSymbol.ContainingType))
        {
            return;
        }

        ReportListCapacityDiagnostic(context);
    }

    private static void RecordListCapacityInitializerReference(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
        {
            helperGraph.RecordForbiddenInitializer(context.ContainingSymbol, ListCapacityForbiddenApiName);
        }
    }

    private static void RecordListCapacityHelperPropertyInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is IPropertySymbol { GetMethod: { } getter } property &&
            !IsEventKernel(property.ContainingType))
        {
            helperGraph.RecordForbidden(getter, ListCapacityForbiddenApiName);
        }
    }

    private static void ReportListCapacityDiagnostic(OperationAnalysisContext context)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            ListCapacityForbiddenApiName));
}
