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
        if (TryGetForbiddenMemberDisplayName(target, out var forbiddenMember))
        {
            ReportAndRecordForbiddenMember(context, helperGraph, method, forbiddenMember);
            return;
        }

        if (!TryGetForbiddenHostApi(target, out var forbiddenType))
        {
            return;
        }

        helperGraph.RecordForbidden(method, forbiddenType);
        if (!IsForbiddenApiRoot(context, method) ||
            !helperGraph.TryRecordDirectDiagnostic(method, forbiddenType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ForbiddenHostApiRule,
            context.Operation.Syntax.GetLocation(),
            forbiddenType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void ReportAndRecordForbiddenMember(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol method,
        string forbiddenMember)
    {
        helperGraph.RecordForbidden(method, forbiddenMember);
        if (IsForbiddenApiRoot(context, method) &&
            helperGraph.TryRecordDirectDiagnostic(method, forbiddenMember))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ForbiddenHostApiRule,
                context.Operation.Syntax.GetLocation(),
                forbiddenMember));
        }
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
        ReportForbiddenReferencedMethodSignature(context, helperGraph, reference.Method);
        RecordDelegateInitializerTarget(context, helperGraph, reference.Method);
        RecordStaticConstructorReachability(context, helperGraph, reference.Method);
        helperGraph.RecordCall(method, reference.Method, context.Operation.Syntax.GetLocation());
        ReportLocalUseIfInvalid(context, reference.Method);
    }
}
