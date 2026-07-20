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
                if (creation.Type is INamedTypeSymbol constructedType)
                {
                    foreach (var containingConstructor in InitializerContainingConstructors(context.ContainingSymbol))
                    {
                        helperGraph.RecordGenericObjectCreation(
                            containingConstructor,
                            initializerConstructor,
                            constructedType,
                            context.Operation.Syntax.GetLocation());
                    }
                }
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
            if (creation.Type is INamedTypeSymbol constructedType)
            {
                helperGraph.RecordGenericObjectCreation(
                    method,
                    constructor,
                    constructedType,
                    context.Operation.Syntax.GetLocation());
            }

            helperGraph.RecordCall(method, constructor, context.Operation.Syntax.GetLocation());
        }
    }

    private static void AnalyzeTypeParameterObjectCreation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        if (context.Operation.Type is not ITypeParameterSymbol typeParameter)
        {
            return;
        }

        if (context.ContainingSymbol is IMethodSymbol method)
        {
            helperGraph.RecordGenericTypeParameterConstruction(method, typeParameter);
            return;
        }

        foreach (var containingConstructor in InitializerContainingConstructors(context.ContainingSymbol))
        {
            helperGraph.RecordGenericTypeParameterConstruction(containingConstructor, typeParameter);
        }
    }

    private static IEnumerable<IMethodSymbol> InitializerContainingConstructors(ISymbol initializer)
    {
        var constructors = initializer switch
        {
            IFieldSymbol { IsStatic: true } field => field.ContainingType.StaticConstructors,
            IFieldSymbol field => field.ContainingType.InstanceConstructors,
            IPropertySymbol { IsStatic: true } property => property.ContainingType.StaticConstructors,
            IPropertySymbol property => property.ContainingType.InstanceConstructors,
            _ => []
        };

        return constructors;
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
