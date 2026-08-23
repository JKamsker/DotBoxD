using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadMemberInspector
{
    public static string? GetUnsupportedPayloadMemberReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowTopLevelAsyncWrapper,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol) =>
        Inspect(
            type,
            role,
            ct,
            allowTopLevelAsyncWrapper,
            allowCurrentTransportShape,
            allowCurrentCancellationToken,
            cancellationTokenSymbol,
            rpcStreamHandleSymbol,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private static string? Inspect(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        bool allowCurrentTaskWrapper,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return Inspect(
                array.ElementType,
                role,
                ct,
                allowCurrentTaskWrapper: false,
                allowCurrentTransportShape: false,
                allowCurrentCancellationToken: false,
                cancellationTokenSymbol,
                rpcStreamHandleSymbol,
                visitedOriginalDefinitions);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (IsTaskLike(named) && allowCurrentTaskWrapper)
        {
            return InspectTypeArguments(
                named,
                role,
                ct,
                allowCurrentTransportShape,
                allowCurrentCancellationToken: false,
                cancellationTokenSymbol,
                rpcStreamHandleSymbol,
                visitedOriginalDefinitions);
        }

        var argumentReason = InspectTypeArguments(
            named,
            role,
            ct,
            allowCurrentTransportShape: false,
            allowCurrentCancellationToken: false,
            cancellationTokenSymbol,
            rpcStreamHandleSymbol,
            visitedOriginalDefinitions);
        if (argumentReason is not null)
        {
            return argumentReason;
        }

        return DtoPayloadMemberInspector.FindUnsupportedMember(
            named,
            role,
            ct,
            visitedOriginalDefinitions,
            (memberType, memberRole, memberCt, memberVisited) =>
                UnsupportedMemberReason(memberType, memberRole, memberCt, cancellationTokenSymbol, rpcStreamHandleSymbol, memberVisited));
    }

    private static string? InspectTypeArguments(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        foreach (var arg in named.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();

            var directReason = RpcTypeValidator.GetUnsupportedDirectTypeReason(
                arg,
                role,
                ct,
                allowTopLevelAsyncWrapper: false,
                allowCurrentTransportShape,
                allowCurrentCancellationToken,
                cancellationTokenSymbol: cancellationTokenSymbol,
                rpcStreamHandleSymbol: rpcStreamHandleSymbol);
            if (directReason is not null)
            {
                return directReason;
            }

            var memberReason = Inspect(
                arg,
                role,
                ct,
                allowCurrentTaskWrapper: false,
                allowCurrentTransportShape,
                allowCurrentCancellationToken,
                cancellationTokenSymbol,
                rpcStreamHandleSymbol,
                visitedOriginalDefinitions);
            if (memberReason is not null)
            {
                return memberReason;
            }
        }

        return null;
    }



    private static string? UnsupportedMemberReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        var directReason = RpcTypeValidator.GetUnsupportedDirectTypeReason(
            type,
            role,
            ct,
            allowTopLevelAsyncWrapper: false,
            cancellationTokenSymbol: cancellationTokenSymbol);
        return directReason ?? Inspect(
            type,
            role,
            ct,
            allowCurrentTaskWrapper: false,
            allowCurrentTransportShape: false,
            allowCurrentCancellationToken: false,
            cancellationTokenSymbol,
            rpcStreamHandleSymbol,
            visitedOriginalDefinitions);
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
        => (type.Name == "Task" || type.Name == "ValueTask") &&
            type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
}
