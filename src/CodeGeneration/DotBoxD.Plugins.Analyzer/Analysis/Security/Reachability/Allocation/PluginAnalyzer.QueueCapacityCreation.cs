using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private const string QueueCapacityForbiddenApiName = "System.Collections.Generic.Queue";
    private const string QueueOriginalDefinitionName = "System.Collections.Generic.Queue<T>";

    private static void ReportAndRecordUnboundedQueueCapacityCreation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IObjectCreationOperation creation)
    {
        if (!IsUnboundedQueueCapacityCreation(creation))
        {
            return;
        }

        if (context.ContainingSymbol is IMethodSymbol method)
        {
            ReportAndRecordQueueCapacityInMethod(context, helperGraph, method);
            return;
        }

        ReportAndRecordQueueCapacityInInitializer(context, helperGraph);
    }

    private static void ReportAndRecordQueueCapacityInMethod(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method)
    {
        helperGraph.RecordForbidden(method, QueueCapacityForbiddenApiName);
        if (IsForbiddenApiRoot(context, method) &&
            helperGraph.TryRecordDirectDiagnostic(method, QueueCapacityForbiddenApiName))
        {
            ReportQueueCapacityDiagnostic(context);
        }
    }

    private static void ReportAndRecordQueueCapacityInInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.ContainingSymbol is not ISymbol initializer ||
            initializer is not (IFieldSymbol or IPropertySymbol))
        {
            return;
        }

        helperGraph.RecordForbiddenInitializer(initializer, QueueCapacityForbiddenApiName);
        if (IsEventKernel(initializer.ContainingType))
        {
            ReportQueueCapacityDiagnostic(context);
        }

        if (initializer is IPropertySymbol { GetMethod: { } getter } property &&
            !IsEventKernel(property.ContainingType))
        {
            helperGraph.RecordForbidden(getter, QueueCapacityForbiddenApiName);
        }
    }

    private static bool IsUnboundedQueueCapacityCreation(IObjectCreationOperation creation)
    {
        if (creation.Type is not INamedTypeSymbol type ||
            !string.Equals(
                type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                QueueOriginalDefinitionName,
                StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter?.Type.SpecialType == SpecialType.System_Int32 &&
                string.Equals(argument.Parameter.Name, "capacity", StringComparison.Ordinal) &&
                IsInt32MaxValue(argument.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInt32MaxValue(IOperation operation)
    {
        var constant = operation.ConstantValue;
        if (constant.HasValue && constant.Value is int value)
        {
            return value == int.MaxValue;
        }

        return operation is IConversionOperation conversion && IsInt32MaxValue(conversion.Operand);
    }

    private static void ReportQueueCapacityDiagnostic(OperationAnalysisContext context)
        => context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            QueueCapacityForbiddenApiName));
}
