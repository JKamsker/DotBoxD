using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeWithExpression(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var operation = (IWithOperation)context.Operation;
        if (operation.CloneMethod is not { } cloneMethod)
        {
            return;
        }

        var target = FindRecordCopyConstructor(cloneMethod.ContainingType) ?? cloneMethod;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, cloneMethod.ContainingType);
            RecordInitializerRootCall(context, helperGraph, target);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, cloneMethod.ContainingType);
        helperGraph.RecordCall(method, target, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, target);
    }

    private static IMethodSymbol? FindRecordCopyConstructor(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type))
            {
                return constructor;
            }
        }

        return null;
    }
}
