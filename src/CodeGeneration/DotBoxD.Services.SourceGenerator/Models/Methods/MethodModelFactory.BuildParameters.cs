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

    private static ParameterBuildResult BuildParameters(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? cancellationTokenSymbol,
        INamedTypeSymbol? rpcStreamHandleSymbol,
        RpcTypeValidationCache validationCache,
        CancellationToken ct,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        var parameters = new List<ParameterModel>();
        var externAliases = new HashSet<string>(System.StringComparer.Ordinal);
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
                rpcStreamHandleSymbol,
                validationCache,
                ct,
                ref cancellationTokenCount,
                ref unsupportedReason,
                ref unsupportedLocation);
            parameters.Add(parameter.Model);
            foreach (var externAlias in parameter.ExternAliases.Array)
            {
                externAliases.Add(externAlias);
            }
            hasCancellationToken |= parameter.IsCancellationToken;
            requiresUnsafeSignature |= parameter.RequiresUnsafeSignature;
        }

        return new ParameterBuildResult(parameters, externAliases.ToEquatableArray(), hasCancellationToken, requiresUnsafeSignature);
    }

    private static ParameterBuildItem BuildParameter(
        IMethodSymbol methodSymbol,
        int parameterIndex,
        INamedTypeSymbol? cancellationTokenSymbol,
        INamedTypeSymbol? rpcStreamHandleSymbol,
        RpcTypeValidationCache validationCache,
        CancellationToken ct,
        ref int cancellationTokenCount,
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation)
    {
        var parameter = methodSymbol.Parameters[parameterIndex];
        var declaredType = GetDeclaredParameterType(parameter, ct);
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
            rpcStreamHandleSymbol,
            validationCache,
            ct,
            parameterLocation,
            ref cancellationTokenCount,
            ref unsupportedReason,
            ref unsupportedLocation);

        return new ParameterBuildItem(
            CreateParameterModel(methodSymbol, parameterIndex, parameter, declaredType.Type, isCancellationToken, streamKind, streamItemType, ct),
            isCancellationToken,
            requiresUnsafeSignature,
            declaredType.ExternAliases);
    }

    private static void ValidateParameter(
        IParameterSymbol parameter,
        bool isCancellationToken,
        ParameterStreamKind streamKind,
        ITypeSymbol? streamItemType,
        INamedTypeSymbol? cancellationTokenSymbol,
        INamedTypeSymbol? rpcStreamHandleSymbol,
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
                rpcStreamHandleSymbol,
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
        string declaredType,
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
            declaredType,
            MethodSignatureFacts.GetCanonicalType(parameter.Type, methodSymbol, ct),
            ParameterRefKindKeyword(parameter.RefKind),
            parameter.IsParams,
            isCancellationToken,
            hasDefaultValue,
            defaultValueLiteral,
            metadataDefaultValueExpression,
            streamKind,
            streamItemType?.ToDisplayString(s_qualifiedFormat),
            MetadataType: declaredType.IndexOf("::", System.StringComparison.Ordinal) >= 0
                ? declaredType
                : TypeOfExpressionFormatter.Format(parameter.Type, ct),
            CallerInfoAttributePrefix: BuildCallerInfoAttributePrefix(
                parameter,
                ct,
                preserveOptionalAttributeDefault,
                preserveMetadataDefaultAttributes: defaultValueLiteral.Length == 0),
            ScopeKeyword: ParameterScopeKeyword(parameter, ct));
    }

    private sealed record ParameterBuildResult(List<ParameterModel> Parameters, EquatableArray<string> ExternAliases, bool HasCancellationToken, bool RequiresUnsafeSignature);

    private sealed record ParameterBuildItem(ParameterModel Model, bool IsCancellationToken, bool RequiresUnsafeSignature, EquatableArray<string> ExternAliases);
}
