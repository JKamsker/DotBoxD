using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static void ValidateMethodReturn(
        string? configuredMethodName,
        ITypeSymbol returnType,
        MethodReturnKind returnKind,
        INamedTypeSymbol? cancellationTokenSymbol,
        INamedTypeSymbol? rpcStreamHandleSymbol,
        RpcTypeValidationCache validationCache,
        CancellationToken ct,
        DiagnosticLocation methodLocation,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            EmptyConfiguredNameReason(configuredMethodName),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            ReturnTypeClassifier.GetUnsupportedServiceReturnReason(returnType, ct),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            UnsupportedReturnTypeReason(returnType, returnKind, cancellationTokenSymbol, rpcStreamHandleSymbol, ct),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            RpcTypeValidator.GetUnsupportedSubServicePayloadReason(
                returnType,
                returnKind,
                "return type",
                ct,
                validationCache),
            methodLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            GetUnsupportedNullableStreamingReturnReason(returnType, returnKind),
            methodLocation);
    }

    private static string? EmptyConfiguredNameReason(string? configuredMethodName)
        => configuredMethodName is not null && string.IsNullOrWhiteSpace(configuredMethodName)
            ? "[RpcMethod(Name = ...)] wire name must not be empty or whitespace"
            : null;

    private static string? UnsupportedReturnTypeReason(
        ITypeSymbol returnType,
        MethodReturnKind returnKind,
        INamedTypeSymbol? cancellationTokenSymbol,
        INamedTypeSymbol? rpcStreamHandleSymbol,
        CancellationToken ct)
        => RpcTypeValidator.GetUnsupportedTypeReason(
            returnType,
            "return type",
            ct,
            allowTopLevelAsyncWrapper: true,
            allowCurrentTransportShape: IsCurrentTransportReturn(returnKind),
            cancellationTokenSymbol: cancellationTokenSymbol,
            rpcStreamHandleSymbol: rpcStreamHandleSymbol);

    private static bool IsCurrentTransportReturn(MethodReturnKind returnKind)
        => NamingHelpers.IsStreamReturn(returnKind) ||
           NamingHelpers.IsPipeReturn(returnKind) ||
           NamingHelpers.IsAsyncEnumerableReturn(returnKind);

    private static void ValidateMethodShape(
        IMethodSymbol methodSymbol,
        DiagnosticLocation methodLocation,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        if (methodSymbol.IsGenericMethod)
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                "generic service methods are not supported; expose a non-generic RPC method instead",
                methodLocation);
        }

        if (methodSymbol.RefKind != RefKind.None)
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                $"return value uses an unsupported pass-by-reference kind '{RefKindDisplay(methodSymbol.RefKind, isReturn: true)}'",
                methodLocation);
        }
    }
}
