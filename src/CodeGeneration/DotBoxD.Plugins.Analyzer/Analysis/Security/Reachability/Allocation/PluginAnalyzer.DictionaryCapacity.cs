using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string DictionaryCapacityForbiddenApiName = "System.Collections.Generic.Dictionary";

    private static void ReportAndRecordDictionaryCapacityCreation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IObjectCreationOperation creation)
    {
        if (!IsDictionaryCapacityConstructor(creation.Constructor))
        {
            return;
        }

        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordForbidden(method, DictionaryCapacityForbiddenApiName);
            if (IsForbiddenApiRoot(context, method) &&
                helperGraph.TryRecordDirectDiagnostic(method, DictionaryCapacityForbiddenApiName))
            {
                ReportDictionaryCapacityDiagnostic(context);
            }

            return;
        }

        ReportDictionaryCapacityInInitializer(context);
        RecordDictionaryCapacityInitializerReference(context, helperGraph);
        RecordDictionaryCapacityHelperPropertyInitializer(context, helperGraph);
    }

    private static bool IsDictionaryCapacityConstructor(IMethodSymbol? constructor)
        => constructor?.ContainingType is INamedTypeSymbol type &&
            type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
            "System.Collections.Generic.Dictionary<TKey, TValue>" &&
            constructor.Parameters.Any(static parameter =>
                parameter.Name == "capacity" &&
                parameter.Type.SpecialType == SpecialType.System_Int32);

    private static void ReportDictionaryCapacityInInitializer(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol &&
            IsEventKernel(context.ContainingSymbol.ContainingType))
        {
            ReportDictionaryCapacityDiagnostic(context);
        }
    }

    private static void RecordDictionaryCapacityInitializerReference(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is IFieldSymbol or IPropertySymbol)
        {
            helperGraph.RecordForbiddenInitializer(
                context.ContainingSymbol,
                DictionaryCapacityForbiddenApiName);
        }
    }

    private static void RecordDictionaryCapacityHelperPropertyInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is IPropertySymbol { GetMethod: { } getter } property &&
            !IsEventKernel(property.ContainingType))
        {
            helperGraph.RecordForbidden(getter, DictionaryCapacityForbiddenApiName);
        }
    }

    private static void ReportDictionaryCapacityDiagnostic(OperationAnalysisContext context)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            DictionaryCapacityForbiddenApiName));
}
