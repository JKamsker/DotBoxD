using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeAnonymousFunction(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var function = (IAnonymousFunctionOperation)context.Operation;
        RecordDelegateInitializerTarget(context, helperGraph, function.Symbol);
    }

    private static void RecordDelegateInitializerTarget(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol target)
    {
        if (TryGetDelegateInitializerField(context, out var field))
        {
            helperGraph.RecordDelegateFieldTarget(field, target);
        }
    }

    private static void RecordForbiddenDelegateInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ITypeSymbol? type)
    {
        if (!IsForbiddenHostApi(type) ||
            !TryGetDelegateInitializerField(context, out var field))
        {
            return;
        }

        helperGraph.RecordForbidden(field, type!);
    }

    private static void RecordDelegateFieldReference(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        IFieldSymbol field)
    {
        if (!IsDelegateType(field.Type))
        {
            return;
        }

        helperGraph.RecordDelegateFieldReference(
            method,
            field,
            context.Operation.Syntax.GetLocation());
    }

    private static bool TryGetDelegateInitializerField(
        OperationAnalysisContext context,
        out IFieldSymbol field)
    {
        if (context.ContainingSymbol is IFieldSymbol containingField &&
            IsDelegateType(containingField.Type))
        {
            field = containingField;
            return true;
        }

        for (var operation = context.Operation.Parent; operation is not null; operation = operation.Parent)
        {
            if (operation is not IFieldInitializerOperation initializer)
            {
                continue;
            }

            foreach (var candidate in initializer.InitializedFields)
            {
                if (IsDelegateType(candidate.Type))
                {
                    field = candidate;
                    return true;
                }
            }
        }

        field = null!;
        return false;
    }

    private static bool IsDelegateType(ITypeSymbol? type)
        => type?.TypeKind == TypeKind.Delegate;
}
