using System;
using System.Collections.Generic;
using System.Linq;
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
        INamedTypeSymbol? rpcStreamHandleSymbol,
        RpcTypeValidationCache validationCache,
        List<MethodDiagnostic> methodDiagnostics,
        CancellationToken ct,
        out DiagnosticLocation methodLocation)
    {
        ct.ThrowIfCancellationRequested();

        var returnType = methodSymbol.ReturnType;
        var declaredReturn = GetDeclaredReturnType(methodSymbol, returnType, ct);
        var returnKind = ReturnTypeClassifier.Classify(returnType, ct, out var unwrappedReturnType, out var subService);
        if (returnKind == MethodReturnKind.Sync && declaredReturn.ExternAliases.Array.Length > 0)
        {
            unwrappedReturnType = declaredReturn.Type;
        }
        var isLookalikeTaskLike = ReturnTypeClassifier.IsLookalikeTaskLike(returnType);
        var metadataTypes = MethodMetadataTypesFactory.Get(methodSymbol, returnKind, ct);
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
            rpcStreamHandleSymbol,
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
            rpcStreamHandleSymbol,
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
        var externAliases = new HashSet<string>(declaredReturn.ExternAliases.Array, StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            foreach (var externAlias in parameter.ExternAliases.Array)
            {
                externAliases.Add(externAlias);
            }
        }

        return new MethodModel(
            Name: IdentifierHelpers.EscapeIdentifier(methodSymbol.Name),
            ExplicitImplementationType: GetExplicitImplementationType(methodSymbol.ContainingType),
            RpcName: LiteralHelpers.EscapeStringLiteral(configuredRpcName),
            ExternAliases: externAliases.ToEquatableArray(),
            ReturnKind: returnKind,
            DeclaredReturnType: declaredReturn.Type,
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
            MetadataResultType: metadataTypes.ResultType,
            IsLookalikeTaskLike: isLookalikeTaskLike);
    }

    internal static string GetExplicitImplementationType(INamedTypeSymbol type) =>
        type.ToDisplayString(s_qualifiedFormat);

    private static (string Type, EquatableArray<string> ExternAliases) GetDeclaredReturnType(
        IMethodSymbol methodSymbol,
        ITypeSymbol returnType,
        CancellationToken ct)
    {
        var declaredReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(ct) is not Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax declaration)
            {
                continue;
            }

            var aliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var externAlias in declaration.SyntaxTree.GetRoot(ct)
                         .DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExternAliasDirectiveSyntax>())
            {
                aliases.Add(externAlias.Identifier.ValueText);
            }

            var usedAliases = new List<string>();
            foreach (var aliasName in declaration.ReturnType
                         .DescendantNodesAndSelf()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax>())
            {
                var alias = aliasName.Alias.Identifier.ValueText;
                if (aliases.Contains(alias) && !usedAliases.Contains(alias, StringComparer.Ordinal))
                {
                    usedAliases.Add(alias);
                }
            }

            if (usedAliases.Count > 0)
            {
                return (declaration.ReturnType.ToString(), usedAliases.ToEquatableArray());
            }
        }

        return (declaredReturnType, EquatableArray<string>.Empty);
    }

    private static (string Type, string MetadataType, EquatableArray<string> ExternAliases) GetDeclaredParameterType(
        IParameterSymbol parameter,
        CancellationToken ct)
    {
        var metadataType = TypeOfExpressionFormatter.Format(parameter.Type, ct);
        foreach (var syntaxReference in parameter.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(ct) is not Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax declaration ||
                declaration.Type is null)
            {
                continue;
            }

            var aliases = GetUsedExternAliases(declaration.Type, ct);
            if (!aliases.IsEmpty)
            {
                return (
                    declaration.Type.ToString(),
                    ApplyExternAliases(metadataType, declaration.Type, ct),
                    aliases);
            }
        }

        return (parameter.Type.ToDisplayString(s_qualifiedFormat), metadataType, EquatableArray<string>.Empty);
    }

    private static EquatableArray<string> GetUsedExternAliases(
        Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax type,
        CancellationToken ct)
    {
        var declaredAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directive in type.SyntaxTree.GetRoot(ct)
                     .DescendantNodes()
                     .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExternAliasDirectiveSyntax>())
        {
            declaredAliases.Add(directive.Identifier.ValueText);
        }

        var usedAliases = new List<string>();
        foreach (var aliasName in type.DescendantNodesAndSelf()
                     .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax>())
        {
            var alias = aliasName.Alias.Identifier.ValueText;
            if (declaredAliases.Contains(alias) && !usedAliases.Contains(alias, StringComparer.Ordinal))
            {
                usedAliases.Add(alias);
            }
        }

        return usedAliases.ToEquatableArray();
    }

    private static string ApplyExternAliases(
        string type,
        Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax declaration,
        CancellationToken ct)
    {
        var searchStart = 0;
        foreach (var aliasName in declaration.DescendantNodesAndSelf()
                     .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            var globalName = ServicesGeneratorTypeNames.GlobalPrefix + aliasName.Name;
            var match = type.IndexOf(globalName, searchStart, StringComparison.Ordinal);
            if (match < 0)
            {
                continue;
            }

            var declaredName = aliasName.ToString();
            type = type.Substring(0, match) + declaredName + type.Substring(match + globalName.Length);
            searchStart = match + declaredName.Length;
        }

        return type;
    }

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
