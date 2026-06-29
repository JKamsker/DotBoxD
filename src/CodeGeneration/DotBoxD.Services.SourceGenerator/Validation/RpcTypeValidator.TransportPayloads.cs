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
        ITypeSymbol? cancellationTokenSymbol)
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
                cancellationTokenSymbol);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsCancellationToken(named, cancellationTokenSymbol))
        {
            return !allowCurrentCancellationToken;
        }

        if (IsTaskLike(named) && allowCurrentTaskWrapper)
        {
            return ContainsTypeArguments(named, ct, allowCurrentTransportShape, cancellationTokenSymbol);
        }

        if (ReturnTypeClassifier.TryGetAsyncEnumerableItemType(named, out _))
        {
            return !allowCurrentTransportShape ||
                ContainsTypeArguments(
                    named,
                    ct,
                    allowCurrentTransportShape: false,
                    cancellationTokenSymbol);
        }

        if (ReturnTypeClassifier.IsStream(named) || ReturnTypeClassifier.IsPipe(named))
        {
            return !allowCurrentTransportShape;
        }

        return ContainsTypeArguments(
            named,
            ct,
            allowCurrentTransportShape: false,
            cancellationTokenSymbol);
    }

    private static bool ContainsTypeArguments(
        INamedTypeSymbol named,
        CancellationToken ct,
        bool allowCurrentTransportShape,
        ITypeSymbol? cancellationTokenSymbol)
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
                    cancellationTokenSymbol))
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
}
