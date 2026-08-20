using System.Threading;
using DotBoxD.Services.SourceGenerator.Models;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static partial class RpcTypeValidator
{
    private static bool ContainsStreamingOrControlPayloadType(
        ITypeSymbol type,
        CancellationToken ct,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        bool allowCurrentTaskWrapper,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return ContainsStreamingOrControlPayloadType(
                array.ElementType,
                ct,
                allowCurrentTransportShape: false,
                allowCurrentCancellationToken: false,
                allowCurrentTaskWrapper: false,
                cancellationTokenSymbol,
                rpcStreamHandleSymbol);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        return ContainsNamedStreamingOrControlPayloadType(
            named,
            ct,
            allowCurrentTransportShape,
            allowCurrentCancellationToken,
            allowCurrentTaskWrapper,
            cancellationTokenSymbol,
            rpcStreamHandleSymbol);
    }

    private static bool ContainsNamedStreamingOrControlPayloadType(
        INamedTypeSymbol named,
        CancellationToken ct,
        bool allowCurrentTransportShape,
        bool allowCurrentCancellationToken,
        bool allowCurrentTaskWrapper,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol)
    {
        if (IsCancellationToken(named, cancellationTokenSymbol))
        {
            return !allowCurrentCancellationToken;
        }

        if (IsRpcStreamHandle(named, rpcStreamHandleSymbol))
        {
            return true;
        }

        if (IsTaskLike(named) && allowCurrentTaskWrapper)
        {
            return ContainsTypeArguments(named, ct, allowCurrentTransportShape, cancellationTokenSymbol, rpcStreamHandleSymbol);
        }

        if (ReturnTypeClassifier.TryGetAsyncEnumerableItemType(named, out _))
        {
            return !allowCurrentTransportShape ||
                ContainsTypeArguments(
                    named,
                    ct,
                    allowCurrentTransportShape: false,
                    cancellationTokenSymbol,
                    rpcStreamHandleSymbol);
        }

        if (ReturnTypeClassifier.IsStream(named) || ReturnTypeClassifier.IsPipe(named))
        {
            return !allowCurrentTransportShape;
        }

        return ContainsTypeArguments(
            named,
            ct,
            allowCurrentTransportShape: false,
            cancellationTokenSymbol,
            rpcStreamHandleSymbol);
    }

    private static bool ContainsTypeArguments(
        INamedTypeSymbol named,
        CancellationToken ct,
        bool allowCurrentTransportShape,
        ITypeSymbol? cancellationTokenSymbol,
        ITypeSymbol? rpcStreamHandleSymbol)
    {
        foreach (var arg in named.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();

            if (ContainsStreamingOrControlPayloadType(
                    arg,
                    ct,
                    allowCurrentTransportShape,
                    allowCurrentCancellationToken: false,
                    allowCurrentTaskWrapper: false,
                    cancellationTokenSymbol,
                    rpcStreamHandleSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCancellationToken(INamedTypeSymbol type, ITypeSymbol? cancellationTokenSymbol) =>
        cancellationTokenSymbol is not null
            ? SymbolEqualityComparer.Default.Equals(type, cancellationTokenSymbol)
            : type.Name == nameof(CancellationToken) && type.ContainingNamespace?.ToDisplayString() == "System.Threading";

    private static bool IsRpcStreamHandle(INamedTypeSymbol type, ITypeSymbol? rpcStreamHandleSymbol) =>
        rpcStreamHandleSymbol is not null &&
        SymbolEqualityComparer.Default.Equals(type, rpcStreamHandleSymbol);

    private static bool ContainsPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IPointerTypeSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsPointerType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsPointerType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsFunctionPointerType(ITypeSymbol type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IFunctionPointerTypeSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsFunctionPointerType(array.ElementType, ct);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();

                if (ContainsFunctionPointerType(arg, ct))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
