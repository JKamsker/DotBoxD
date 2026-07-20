using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Generation;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static MethodModel Build(
        string displayName,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol? cancellationTokenSymbol,
        RpcTypeValidationCache validationCache,
        List<MethodDiagnostic> methodDiagnostics,
        CancellationToken ct,
        out DiagnosticLocation methodLocation)
    {
        ct.ThrowIfCancellationRequested();

        var returnType = methodSymbol.ReturnType;
        var returnKind = ReturnTypeClassifier.Classify(returnType, ct, out var unwrappedReturnType, out var subService);
        var metadataTypes = MethodMetadataTypesFactory.Get(methodSymbol, returnKind, ct);
        var declaredReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        var typeParameterList = MethodSignatureFormatter.GetTypeParameterList(methodSymbol, ct);
        var constraintClauses = MethodSignatureFormatter.GetConstraintClauses(methodSymbol, ct);
        string? unsupportedReason = null;
        methodLocation = DiagnosticLocationFactory.FromSymbol(methodSymbol);
        var unsupportedLocation = methodLocation;
        var requiresUnsafeSignature = RpcTypeValidator.RequiresUnsafeContext(returnType, ct);

        // An explicit empty/whitespace [RpcMethod(Name = "")] compiles but throws ArgumentException on
        // the first call (the empty wire name fails validation), so reject it at build time.
        var configuredMethodName = GetConfiguredMethodName(methodSymbol);
        if (configuredMethodName is not null && string.IsNullOrWhiteSpace(configuredMethodName))
        {
            SetUnsupported(
                ref unsupportedReason,
                ref unsupportedLocation,
                "[RpcMethod(Name = ...)] wire name must not be empty or whitespace",
                methodLocation);
        }

        ValidateMethodReturn(
            configuredMethodName,
            returnType,
            returnKind,
            cancellationTokenSymbol,
            validationCache,
            ct,
            methodLocation,
            ref unsupportedReason,
            ref unsupportedLocation);
        ValidateMethodShape(
            methodSymbol,
            methodLocation,
            ref unsupportedReason,
            ref unsupportedLocation);
        var parameterResult = BuildParameters(
            methodSymbol,
            cancellationTokenSymbol,
            validationCache,
            ct,
            ref unsupportedReason,
            ref unsupportedLocation);
        var parameters = parameterResult.Parameters;
        var hasCancellationToken = parameterResult.HasCancellationToken;
        requiresUnsafeSignature |= parameterResult.RequiresUnsafeSignature;

        if (unsupportedReason is not null)
        {
            methodDiagnostics.Add(new MethodDiagnostic(
                displayName,
                methodSymbol.Name,
                unsupportedReason,
                unsupportedLocation));
        }

        var configuredRpcName = configuredMethodName ?? methodSymbol.Name;

        return new MethodModel(
            Name: IdentifierHelpers.EscapeIdentifier(methodSymbol.Name),
            ExplicitImplementationType: GetExplicitImplementationType(methodSymbol.ContainingType),
            RpcName: LiteralHelpers.EscapeStringLiteral(configuredRpcName),
            ReturnKind: returnKind,
            DeclaredReturnType: declaredReturnType,
            UnwrappedReturnType: unwrappedReturnType,
            MemberAttributePrefix: MemberAttributeFormatter.BuildPrefix(methodSymbol, ct) +
                BuildMemberAttributePrefix(methodSymbol, ct),
            ReturnRefKindKeyword: ReturnRefKindKeyword(methodSymbol.RefKind),
            ReturnAttributePrefix: BuildReturnFlowAttributePrefix(methodSymbol, ct),
            HasCancellationToken: hasCancellationToken,
            Parameters: parameters.ToEquatableArray(),
            AdditionalExplicitImplementationTypes: EquatableArray<string>.Empty,
            RequiresUnsafeSignature: requiresUnsafeSignature,
            TypeParameterCount: methodSymbol.Arity,
            TypeParameterList: typeParameterList,
            ConstraintClauses: constraintClauses,
            UnsupportedReason: unsupportedReason,
            SubService: subService,
            RawRpcName: configuredRpcName,
            MetadataReturnType: metadataTypes.ReturnType,
            MetadataResultType: metadataTypes.ResultType);
    }

    internal static string GetExplicitImplementationType(INamedTypeSymbol type) =>
        type.ToDisplayString(s_qualifiedFormat);

    private static string? GetConfiguredMethodName(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (!ServicesGeneratorTypeNames.IsRpcMethodAttribute(attr.AttributeClass))
            {
                continue;
            }

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    return s;
                }
            }
        }

        return null;
    }

}
