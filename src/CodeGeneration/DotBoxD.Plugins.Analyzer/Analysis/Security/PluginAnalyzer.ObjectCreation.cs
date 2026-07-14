using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeObjectCreation(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            if (ReportAndRecordValueTaskPayloadInInitializer(context, helperGraph, creation.Type))
            {
                return;
            }

            ReportForbiddenInInitializer(context, creation.Type);
            RecordForbiddenInitializerReference(context, helperGraph, creation.Type);
            RecordForbiddenDelegateInitializer(context, helperGraph, creation.Type);
            RecordStaticConstructorReachability(context, helperGraph, creation.Type);
            RecordFinalizerReachability(context, helperGraph, creation.Type);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, creation.Type);
            if (creation.Constructor is { } initializerConstructor)
            {
                helperGraph.RecordConstructorInitializers(initializerConstructor);
                RecordInitializerRootCall(context, helperGraph, initializerConstructor);
            }

            return;
        }

        if (ReportAndRecordValueTaskPayloadIfForbidden(context, helperGraph, method, creation.Type))
        {
            return;
        }

        ReportAndRecordIfForbidden(
            context,
            helperGraph,
            method,
            creation.Constructor ?? (ISymbol?)creation.Type);
        RecordStaticConstructorReachability(context, helperGraph, creation.Type);
        RecordFinalizerReachability(context, helperGraph, creation.Type);
        if (creation.Constructor is { } constructor)
        {
            helperGraph.RecordConstructorInitializers(constructor);
            helperGraph.RecordCall(method, constructor, context.Operation.Syntax.GetLocation());
        }
    }

    private static bool IsValueTaskObjectCreation(ITypeSymbol? type, Compilation compilation)
        => type is not null && DotBoxDWellKnownTaskTypes.IsValueTask(type, compilation);

    private static bool ReportAndRecordValueTaskPayloadIfForbidden(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ITypeSymbol? type)
    {
        if (!IsValueTaskObjectCreation(type, context.Compilation))
        {
            return false;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                ReportAndRecordIfForbidden(context, helperGraph, method, argument);
            }
        }

        return true;
    }

    private static bool ReportAndRecordValueTaskPayloadInInitializer(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        ITypeSymbol? type)
    {
        if (!IsValueTaskObjectCreation(type, context.Compilation))
        {
            return false;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                ReportForbiddenInInitializer(context, argument);
                RecordForbiddenInitializerReference(context, helperGraph, argument);
                RecordForbiddenHelperPropertyInitializer(context, helperGraph, argument);
            }
        }

        return true;
    }
}
