using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void AnalyzeDynamicMemberReference(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var memberReference = (IDynamicMemberReferenceOperation)context.Operation;
        if (memberReference.Parent is IDynamicInvocationOperation invocation &&
            ReferenceEquals(invocation.Operation, memberReference))
        {
            return;
        }

        if (TryGetDynamicReceiverType(memberReference.Instance, out var receiverType))
        {
            var (usesGetter, usesSetter) = AccessorUsage(memberReference);
            if (usesGetter)
            {
                foreach (var target in DynamicPropertyGetterCandidates(receiverType, memberReference.MemberName))
                {
                    AnalyzeResolvedDynamicTarget(context, helperGraph, target);
                }
            }

            if (usesSetter)
            {
                foreach (var target in DynamicPropertySetterCandidates(receiverType, memberReference.MemberName))
                {
                    AnalyzeResolvedDynamicTarget(context, helperGraph, target);
                }
            }

            return;
        }

        if (TryGetDynamicReceiverLocal(memberReference.Instance, out var local))
        {
            var (usesGetter, usesSetter) = AccessorUsage(memberReference);
            helperGraph.RecordDynamicPropertyReference(
                context.ContainingSymbol,
                local,
                memberReference.MemberName,
                usesGetter,
                usesSetter,
                context.Operation.Syntax.GetLocation());
        }
    }

    private static void AnalyzeDynamicIndexerAccess(
        OperationAnalysisContext context,
        ForbiddenHelperCallGraph helperGraph)
    {
        var indexer = (IDynamicIndexerAccessOperation)context.Operation;
        if (TryGetDynamicReceiverType(indexer.Operation, out var receiverType))
        {
            var (usesGetter, usesSetter) = AccessorUsage(indexer);
            if (usesGetter)
            {
                foreach (var target in DynamicIndexerGetterCandidates(receiverType, indexer.Arguments.Length))
                {
                    AnalyzeResolvedDynamicTarget(context, helperGraph, target);
                }
            }

            if (usesSetter)
            {
                foreach (var target in DynamicIndexerSetterCandidates(receiverType, indexer.Arguments.Length))
                {
                    AnalyzeResolvedDynamicTarget(context, helperGraph, target);
                }
            }

            return;
        }

        if (TryGetDynamicReceiverLocal(indexer.Operation, out var local))
        {
            var (usesGetter, usesSetter) = AccessorUsage(indexer);
            helperGraph.RecordDynamicIndexerAccess(
                context.ContainingSymbol,
                local,
                indexer.Arguments.Length,
                usesGetter,
                usesSetter,
                context.Operation.Syntax.GetLocation());
        }
    }

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

        if (TryGetDynamicReceiverLocal(memberReference.Instance, out var local))
        {
            helperGraph.RecordDynamicInvocation(
                context.ContainingSymbol,
                local,
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
            ReportForbiddenReferencedMethodSignature(context, helperGraph, target);
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
        return DynamicCandidateMembers(receiverType, memberName)
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary)
            .Where(method => method.TypeParameters.Length == 0)
            .Where(method => CanAcceptArgumentCount(method, argumentCount));
    }

    internal static IEnumerable<IMethodSymbol> DynamicPropertyGetterCandidates(
        ITypeSymbol receiverType,
        string memberName)
    {
        return DynamicCandidateMembers(receiverType, memberName)
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsIndexer)
            .Select(property => property.GetMethod)
            .Where(getter => getter is not null)!;
    }

    internal static IEnumerable<IMethodSymbol> DynamicPropertySetterCandidates(
        ITypeSymbol receiverType,
        string memberName)
    {
        return DynamicCandidateMembers(receiverType, memberName)
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsIndexer)
            .Select(property => property.SetMethod)
            .Where(setter => setter is not null)!;
    }

    internal static IEnumerable<IMethodSymbol> DynamicIndexerGetterCandidates(
        ITypeSymbol receiverType,
        int argumentCount)
    {
        return DynamicCandidateMembers(receiverType)
            .OfType<IPropertySymbol>()
            .Where(property => property.IsIndexer && CanAcceptArgumentCount(property, argumentCount))
            .Select(property => property.GetMethod)
            .Where(getter => getter is not null)!;
    }

    internal static IEnumerable<IMethodSymbol> DynamicIndexerSetterCandidates(
        ITypeSymbol receiverType,
        int argumentCount)
    {
        return DynamicCandidateMembers(receiverType)
            .OfType<IPropertySymbol>()
            .Where(property => property.IsIndexer && CanAcceptArgumentCount(property, argumentCount))
            .Select(property => property.SetMethod)
            .Where(setter => setter is not null)!;
    }

    private static IEnumerable<ISymbol> DynamicCandidateMembers(ITypeSymbol receiverType, string? memberName = null)
    {
        var seenMembers = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var type in DynamicCandidateTypes(receiverType))
        {
            var members = memberName is null ? type.GetMembers() : type.GetMembers(memberName);
            foreach (var member in members)
            {
                if (seenMembers.Add(member))
                {
                    yield return member;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> DynamicCandidateTypes(ITypeSymbol receiverType)
    {
        if (receiverType is not INamedTypeSymbol namedType)
        {
            yield break;
        }

        for (var current = namedType; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        if (namedType.TypeKind == TypeKind.Interface)
        {
            foreach (var implementedInterface in namedType.AllInterfaces)
            {
                yield return implementedInterface;
            }
        }
    }

    private static bool CanAcceptArgumentCount(IMethodSymbol method, int argumentCount)
        => CanAcceptArgumentCount(method.Parameters, argumentCount);

    private static bool CanAcceptArgumentCount(IPropertySymbol property, int argumentCount)
        => CanAcceptArgumentCount(property.Parameters, argumentCount);

    private static bool CanAcceptArgumentCount(ImmutableArray<IParameterSymbol> parameters, int argumentCount)
    {
        var required = parameters.Count(parameter => !parameter.IsOptional && !parameter.IsParams);
        return argumentCount >= required &&
            (argumentCount <= parameters.Length ||
                parameters.LastOrDefault()?.IsParams == true);
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

    private static bool TryGetDynamicReceiverLocal(
        IOperation? receiver,
        out ILocalSymbol local)
    {
        if (receiver is ILocalReferenceOperation localReference)
        {
            local = localReference.Local;
            return true;
        }

        if (receiver is IConversionOperation conversion)
        {
            return TryGetDynamicReceiverLocal(conversion.Operand, out local);
        }

        local = null!;
        return false;
    }
}
