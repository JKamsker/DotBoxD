using System.Collections.Generic;
using System.Threading;
using DotBoxD.CodeGeneration.Shared.Defaults;
using DotBoxD.Services.SourceGenerator.Generation;
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
            UnsupportedReturnTypeReason(returnType, returnKind, cancellationTokenSymbol, ct),
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
        CancellationToken ct)
        => RpcTypeValidator.GetUnsupportedTypeReason(
            returnType,
            "return type",
            ct,
            allowTopLevelAsyncWrapper: true,
            allowCurrentTransportShape: IsCurrentTransportReturn(returnKind),
            cancellationTokenSymbol: cancellationTokenSymbol);

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

    private static ParameterBuildResult BuildParameters(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? cancellationTokenSymbol,
        RpcTypeValidationCache validationCache,
        CancellationToken ct,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        var parameters = new List<ParameterModel>();
        var hasCancellationToken = false;
        var cancellationTokenCount = 0;
        var requiresUnsafeSignature = false;
        for (var parameterIndex = 0; parameterIndex < methodSymbol.Parameters.Length; parameterIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var parameter = BuildParameter(
                methodSymbol,
                parameterIndex,
                cancellationTokenSymbol,
                validationCache,
                ct,
                ref cancellationTokenCount,
                ref unsupportedReason,
                ref unsupportedLocation);
            parameters.Add(parameter.Model);
            hasCancellationToken |= parameter.IsCancellationToken;
            requiresUnsafeSignature |= parameter.RequiresUnsafeSignature;
        }

        return new ParameterBuildResult(parameters, hasCancellationToken, requiresUnsafeSignature);
    }

    private static ParameterBuildItem BuildParameter(
        IMethodSymbol methodSymbol,
        int parameterIndex,
        INamedTypeSymbol? cancellationTokenSymbol,
        RpcTypeValidationCache validationCache,
        CancellationToken ct,
        ref int cancellationTokenCount,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        var parameter = methodSymbol.Parameters[parameterIndex];
        var parameterLocation = DiagnosticLocationFactory.FromSymbol(parameter);
        var requiresUnsafeSignature = RpcTypeValidator.RequiresUnsafeContext(parameter.Type, ct);
        var isCancellationToken = cancellationTokenSymbol is not null &&
            SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationTokenSymbol);
        var (streamKind, streamItemType) = ClassifyParameterStream(parameter.Type, ct);
        ValidateParameter(
            parameter,
            isCancellationToken,
            streamKind,
            streamItemType,
            cancellationTokenSymbol,
            validationCache,
            ct,
            parameterLocation,
            ref cancellationTokenCount,
            ref unsupportedReason,
            ref unsupportedLocation);

        return new ParameterBuildItem(
            CreateParameterModel(methodSymbol, parameterIndex, parameter, isCancellationToken, streamKind, streamItemType, ct),
            isCancellationToken,
            requiresUnsafeSignature);
    }

    private static void ValidateParameter(
        IParameterSymbol parameter,
        bool isCancellationToken,
        ParameterStreamKind streamKind,
        ITypeSymbol? streamItemType,
        INamedTypeSymbol? cancellationTokenSymbol,
        RpcTypeValidationCache validationCache,
        CancellationToken ct,
        DiagnosticLocation parameterLocation,
        ref int cancellationTokenCount,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        ValidateCancellationTokenParameter(isCancellationToken, ref cancellationTokenCount, parameterLocation, ref unsupportedReason, ref unsupportedLocation);
        if (parameter.RefKind != RefKind.None)
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                $"parameter '{parameter.Name}' uses an unsupported pass-by-reference kind '{RefKindDisplay(parameter.RefKind, isReturn: false)}'",
                parameterLocation);
        }

        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            GetUnsupportedParameterTypeReason(
                parameter.Type,
                streamKind,
                streamItemType,
                parameter.Name,
                isCancellationToken,
                cancellationTokenSymbol,
                ct),
            parameterLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            GetUnsupportedParameterSubServiceReason(
                parameter.Type,
                streamKind,
                streamItemType,
                parameter.Name,
                ct,
                validationCache),
            parameterLocation);
        SetUnsupported(
            ref unsupportedReason,
            ref unsupportedLocation,
            GetUnsupportedNullableStreamingParameterReason(parameter.Type, streamKind, parameter.Name),
            parameterLocation);
    }

    private static void ValidateCancellationTokenParameter(
        bool isCancellationToken,
        ref int cancellationTokenCount,
        DiagnosticLocation parameterLocation,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        if (!isCancellationToken)
        {
            return;
        }

        cancellationTokenCount++;
        if (cancellationTokenCount > 1)
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                "multiple CancellationToken parameters are not supported",
                parameterLocation);
        }
    }

    private static ParameterModel CreateParameterModel(
        IMethodSymbol methodSymbol,
        int parameterIndex,
        IParameterSymbol parameter,
        bool isCancellationToken,
        ParameterStreamKind streamKind,
        ITypeSymbol? streamItemType,
        CancellationToken ct)
    {
        var hasDefaultValue = ParameterDefaultValueEmitter.HasDefaultValue(parameter);
        var preserveOptionalAttributeDefault =
            ParameterDefaultValueEmitter.ShouldPreserveOptionalAttributeDefault(methodSymbol, parameterIndex);
        var defaultValueLiteral = isCancellationToken || preserveOptionalAttributeDefault
            ? string.Empty
            : ParameterDefaultValueEmitter.FormatSignatureDefaultLiteral(
                parameter,
                hasDefaultValue,
                DefaultLiteralOptions.SourceGenerator) ?? string.Empty;
        var metadataDefaultValueExpression = isCancellationToken
            ? string.Empty
            : ParameterDefaultValueEmitter.FormatMetadataDefaultValueExpression(
                parameter,
                hasDefaultValue,
                defaultValueLiteral);

        return new ParameterModel(
            IdentifierHelpers.EscapeIdentifier(parameter.Name),
            parameter.Type.ToDisplayString(s_qualifiedFormat),
            MethodSignatureFacts.GetCanonicalType(parameter.Type, methodSymbol, ct),
            ParameterRefKindKeyword(parameter.RefKind),
            parameter.IsParams,
            isCancellationToken,
            hasDefaultValue,
            defaultValueLiteral,
            metadataDefaultValueExpression,
            streamKind,
            streamItemType?.ToDisplayString(s_qualifiedFormat),
            MetadataType: TypeOfExpressionFormatter.Format(parameter.Type, ct),
            CallerInfoAttributePrefix: BuildCallerInfoAttributePrefix(
                parameter,
                ct,
                preserveOptionalAttributeDefault,
                preserveMetadataDefaultAttributes: defaultValueLiteral.Length == 0),
            ScopeKeyword: ParameterScopeKeyword(parameter, ct));
    }

    private sealed record ParameterBuildResult(List<ParameterModel> Parameters, bool HasCancellationToken, bool RequiresUnsafeSignature);

    private sealed record ParameterBuildItem(ParameterModel Model, bool IsCancellationToken, bool RequiresUnsafeSignature);
}
