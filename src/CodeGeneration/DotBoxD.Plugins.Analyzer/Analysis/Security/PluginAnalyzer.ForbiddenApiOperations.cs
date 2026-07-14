using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void ReportAndRecordIfForbidden(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        ISymbol? target)
    {
        if (!TryGetForbiddenHostApi(target, out var forbidden))
        {
            return;
        }

        helperGraph.RecordForbidden(method, forbidden);
        if (!IsEventKernel(method.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            forbidden.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void AnalyzeTypeOf(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var type = ((ITypeOfOperation)context.Operation).TypeOperand;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, type);
            RecordForbiddenInitializerReference(context, helperGraph, type);
            RecordForbiddenDelegateInitializer(context, helperGraph, type);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, type);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, type);
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context, ForbiddenHelperCallGraph helperGraph)
    {
        var reference = (IMethodReferenceOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol method)
        {
            ReportForbiddenInInitializer(context, reference.Method);
            RecordForbiddenInitializerReference(context, helperGraph, reference.Method);
            RecordForbiddenDelegateInitializer(context, helperGraph, reference.Method.ContainingType);
            RecordDelegateInitializerTarget(context, helperGraph, reference.Method);
            RecordStaticConstructorReachability(context, helperGraph, reference.Method);
            RecordForbiddenHelperPropertyInitializer(context, helperGraph, reference.Method);
            RecordInitializerRootCall(context, helperGraph, reference.Method);
            return;
        }

        ReportAndRecordIfForbidden(context, helperGraph, method, reference.Method);
        ReportForbiddenReferencedMethodSignature(context, reference.Method);
        RecordDelegateInitializerTarget(context, helperGraph, reference.Method);
        RecordStaticConstructorReachability(context, helperGraph, reference.Method);
        helperGraph.RecordCall(method, reference.Method, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, reference.Method);
    }
}
