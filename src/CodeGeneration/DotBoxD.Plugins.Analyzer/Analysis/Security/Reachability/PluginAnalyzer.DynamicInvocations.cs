using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeDynamicInvocation(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var invocation = (IDynamicInvocationOperation)context.Operation;
        if (invocation.Operation is not IDynamicMemberReferenceOperation memberReference)
        {
            return;
        }

        if (TryGetDynamicReceiverType(memberReference.Instance, out var receiverType))
        {
            foreach (var target in DynamicInvocationCandidates(receiverType, memberReference.MemberName, invocation))
            {
                AnalyzeResolvedDynamicTarget(context, helperGraph, target);
            }

            return;
        }

        if (memberReference.Instance is ILocalReferenceOperation localReference)
        {
            helperGraph.RecordDynamicInvocation(
                context.ContainingSymbol,
                localReference.Local,
                memberReference.MemberName,
                invocation.Arguments.Length,
                context.Operation.Syntax.GetLocation());
        }
    }

    private static void AnalyzeResolvedDynamicTarget(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph,
        IMethodSymbol target)
    {
        if (context.ContainingSymbol is IMethodSymbol method)
        {
            ReportAndRecordIfForbidden(context, helperGraph, method, target.ContainingType);
            RecordStaticConstructorReachability(context, helperGraph, target);
            ReportForbiddenReferencedMethodSignature(context, target);
            helperGraph.RecordCall(method, target, context.Operation.Syntax.GetLocation());
            ReportLocalUseIfInvalid(context, target);
            return;
        }

        ReportForbiddenInInitializer(context, target.ContainingType);
        RecordForbiddenInitializerReference(context, helperGraph, target.ContainingType);
        RecordForbiddenDelegateInitializer(context, helperGraph, target.ContainingType);
        RecordStaticConstructorReachability(context, helperGraph, target);
        RecordForbiddenHelperPropertyInitializer(context, helperGraph, target.ContainingType);
        RecordInitializerRootCall(context, helperGraph, target);
    }

    internal static IEnumerable<IMethodSymbol> DynamicInvocationCandidates(
        ITypeSymbol receiverType,
        string memberName,
        IDynamicInvocationOperation invocation)
        => DynamicInvocationCandidates(receiverType, memberName, invocation.Arguments.Length);

    internal static IEnumerable<IMethodSymbol> DynamicInvocationCandidates(
        ITypeSymbol receiverType,
        string memberName,
        int argumentCount)
    {
        return receiverType
            .GetMembers(memberName)
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary)
            .Where(method => method.TypeParameters.Length == 0)
            .Where(method => CanAcceptArgumentCount(method, argumentCount));
    }

    private static bool CanAcceptArgumentCount(IMethodSymbol method, int argumentCount)
    {
        var required = method.Parameters.Count(parameter => !parameter.IsOptional && !parameter.IsParams);
        return argumentCount >= required &&
            (argumentCount <= method.Parameters.Length ||
                method.Parameters.LastOrDefault()?.IsParams == true);
    }

    private static bool TryGetDynamicReceiverType(
        IOperation? receiver,
        out ITypeSymbol type)
    {
        if (receiver?.Type is { TypeKind: not TypeKind.Dynamic } receiverType)
        {
            type = receiverType;
            return true;
        }

        if (receiver is IConversionOperation conversion)
        {
            return TryGetDynamicReceiverType(conversion.Operand, out type);
        }

        type = null!;
        return false;
    }
}
