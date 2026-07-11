using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainStageIrSignatureFactory
{
    public static HookChainStageIrSignature Create(
        HookChainStage stage,
        ITypeSymbol inputType,
        string outputTypeFullName,
        INamedTypeSymbol eventType,
        GeneratedRemoteHookChainKind? generatedRemoteKind,
        string? generatedRemoteServerContextTypeFullName,
        bool receiverIsStage,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(stage.Invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return generatedRemoteKind is { } kind
                ? HookChainStageIrGeneratedRemoteSignatureFactory.Create(
                    stage,
                    inputType,
                    outputTypeFullName,
                    eventType,
                    kind,
                    generatedRemoteServerContextTypeFullName,
                    receiverIsStage)
                : throw new NotSupportedException("the stage method could not be resolved.");
        }

        var receiverType = ReceiverType(stage.Invocation, model, cancellationToken);
        var substitution = RequiresGenericInterceptor(
                receiverType,
                method.Parameters[0].Type,
                method.ReturnType,
                method.Parameters[1].Type)
            ? GenericContextSubstitution(receiverType, method)
            : TypeParameterSubstitution.Empty;

        return new HookChainStageIrSignature(
            RewriteWithTypeParameters(receiverType, substitution.Map),
            RewriteWithTypeParameters(method.Parameters[0].Type, substitution.Map),
            RewriteWithTypeParameters(method.ReturnType, substitution.Map),
            RewriteWithTypeParameters(method.Parameters[1].Type, substitution.Map),
            MethodTypeArguments(method, substitution.Map),
            substitution.TypeParameters,
            substitution.TypeArguments,
            method.Name,
            method.Parameters[0].Name,
            method.Parameters[1].Name);
    }

    private static ITypeSymbol ReceiverType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member &&
            model.GetTypeInfo(member.Expression, cancellationToken).Type is { } receiverType)
        {
            return receiverType;
        }

        throw new NotSupportedException("the stage method must be called on an instance receiver.");
    }

    private static string MethodTypeArguments(
        IMethodSymbol method,
        IReadOnlyDictionary<ISymbol, string> substitution)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        return "<" + string.Join(", ", method.TypeArguments.Select(type =>
            RewriteWithTypeParameters(type, substitution))) + ">";
    }

    private static bool RequiresGenericInterceptor(params ITypeSymbol[] roots)
    {
        foreach (var root in roots)
        {
            if (ContainsAnonymousType(root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnonymousType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsAnonymousType: true })
        {
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (ContainsAnonymousType(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static TypeParameterSubstitution GenericContextSubstitution(
        ITypeSymbol receiverType,
        IMethodSymbol method)
    {
        var map = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var parameters = new List<string>();
        if (receiverType is INamedTypeSymbol namedReceiver)
        {
            AddTypeParameters(namedReceiver.TypeArguments, namedReceiver.ConstructedFrom.TypeParameters, map, parameters);
        }

        AddTypeParameters(method.TypeArguments, method.TypeParameters, map, parameters);
        var typeParameters = string.Join(", ", parameters);
        return new TypeParameterSubstitution(
            map,
            typeParameters.Length == 0 ? null : typeParameters,
            typeParameters.Length == 0 ? string.Empty : "<" + typeParameters + ">");
    }

    private static void AddTypeParameters(
        IReadOnlyList<ITypeSymbol> typeArguments,
        IReadOnlyList<ITypeParameterSymbol> typeParameters,
        Dictionary<ISymbol, string> map,
        List<string> parameters)
    {
        for (var i = 0; i < typeArguments.Count && i < typeParameters.Count; i++)
        {
            var name = typeParameters[i].Name;
            if (map.ContainsKey(typeArguments[i]))
            {
                continue;
            }

            map[typeArguments[i]] = name;
            parameters.Add(name);
        }
    }

    private static string RewriteWithTypeParameters(
        ITypeSymbol type,
        IReadOnlyDictionary<ISymbol, string> substitution)
    {
        if (substitution.TryGetValue(type, out var parameterName))
        {
            return parameterName;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var prefix = named.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : named.ContainingNamespace.ToDisplayString() + ".";
        var arguments = new List<string>(named.TypeArguments.Length);
        foreach (var argument in named.TypeArguments)
        {
            arguments.Add(RewriteWithTypeParameters(argument, substitution));
        }

        return DotBoxDGenerationNames.TypeNames.GlobalPrefix + prefix + named.Name +
            "<" + string.Join(", ", arguments) + ">";
    }

    private sealed record TypeParameterSubstitution(
        IReadOnlyDictionary<ISymbol, string> Map,
        string? TypeParameters,
        string TypeArguments)
    {
        public static TypeParameterSubstitution Empty { get; } = new(
            new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default),
            null,
            string.Empty);
    }
}

internal sealed record HookChainStageIrSignature(
    string ReceiverType,
    string DelegateType,
    string ReturnType,
    string IRFuncType,
    string MethodTypeArguments,
    string? TypeParameters,
    string TypeArguments,
    string MethodName,
    string SourceParameterName,
    string IRParameterName);
