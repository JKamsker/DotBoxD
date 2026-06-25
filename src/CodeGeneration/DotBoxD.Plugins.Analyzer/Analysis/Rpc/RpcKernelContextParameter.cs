using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelContextParameter
{
    private const string GeneratedPluginServerRegistryAttribute =
        "DotBoxD.Abstractions.GeneratedPluginServerRegistryAttribute";
    private const string GeneratePluginServerAttribute =
        "DotBoxD.Abstractions.GeneratePluginServerAttribute";

    public static bool IsSupported(IParameterSymbol parameter, Compilation compilation)
        => parameter.RefKind == RefKind.None &&
           (IsRawHookContext(parameter.Type) ||
            IsGeneratedContext(parameter.Type, compilation));

    private static bool IsRawHookContext(ITypeSymbol type)
        => string.Equals(type.ToDisplayString(), DotBoxDMetadataNames.HookContextType, StringComparison.Ordinal);

    private static bool IsGeneratedContext(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly)
            ? IsSameCompilationGeneratedContext(named, compilation)
            : IsMarkedGeneratedContext(named, compilation);
    }

    private static bool IsMarkedGeneratedContext(INamedTypeSymbol contextType, Compilation compilation)
    {
        if (!ReferencesGeneratedRegistryContract(contextType.ContainingAssembly))
        {
            return false;
        }

        foreach (var registryType in TypesInNamespace(contextType.ContainingAssembly.GlobalNamespace))
        {
            if (registryType.DeclaringSyntaxReferences.Length == 0 &&
                IsGeneratedRegistryForContext(registryType, contextType, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferencesGeneratedRegistryContract(IAssemblySymbol assembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var referenced in module.ReferencedAssemblySymbols)
            {
                if (string.Equals(referenced.Identity.Name, "DotBoxD.Abstractions", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return string.Equals(assembly.Identity.Name, "DotBoxD.Abstractions", StringComparison.Ordinal);
    }

    private static IEnumerable<INamedTypeSymbol> TypesInNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in NestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in TypesInNamespace(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in NestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsGeneratedRegistryForContext(
        INamedTypeSymbol registryType,
        INamedTypeSymbol contextType,
        Compilation compilation)
    {
        foreach (var attribute in registryType.GetAttributes())
        {
            if (ReadRegistryMarker(attribute, compilation) is { } marker &&
                marker.ContextType is { } markedContext &&
                SymbolEqualityComparer.Default.Equals(markedContext, contextType) &&
                SymbolEqualityComparer.Default.Equals(registryType.ContainingAssembly, contextType.ContainingAssembly) &&
                MarkerOwnershipMatches(registryType, marker.ServerType, contextType, marker.Kind, compilation) &&
                RegistryOnShapeMatches(registryType, contextType, marker.Kind))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameCompilationGeneratedContext(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var symbol in compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type))
        {
            if (symbol is INamedTypeSymbol server &&
                GeneratedContextType(server, compilation) is { } generatedContext &&
                SymbolEqualityComparer.Default.Equals(generatedContext, type))
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? GeneratedContextType(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!IsDotBoxDAttribute(attribute, compilation, GeneratePluginServerAttribute))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "Context", StringComparison.Ordinal) &&
                    argument.Value.Value is INamedTypeSymbol contextType)
                {
                    return contextType;
                }
            }
        }

        return null;
    }

    private static RegistryMarker? ReadRegistryMarker(AttributeData attribute, Compilation compilation)
    {
        if (!IsDotBoxDAttribute(attribute, compilation, GeneratedPluginServerRegistryAttribute) ||
            attribute.ConstructorArguments.Length != 3 ||
            RegistryKind(attribute.ConstructorArguments[0]) is not { } kind ||
            attribute.ConstructorArguments[1].Value is not INamedTypeSymbol serverType ||
            attribute.ConstructorArguments[2].Value is not INamedTypeSymbol contextType)
        {
            return null;
        }

        return new RegistryMarker(kind, serverType, contextType);
    }

    private static GeneratedRemoteRegistryKind? RegistryKind(TypedConstant value)
        => value.Value switch
        {
            0 => GeneratedRemoteRegistryKind.Hook,
            1 => GeneratedRemoteRegistryKind.Subscription,
            _ => null
        };

    private static bool MarkerOwnershipMatches(
        INamedTypeSymbol registryType,
        INamedTypeSymbol serverType,
        INamedTypeSymbol contextType,
        GeneratedRemoteRegistryKind kind,
        Compilation compilation)
    {
        if (serverType.TypeKind != TypeKind.Class ||
            !SymbolEqualityComparer.Default.Equals(GeneratedContextType(serverType, compilation), contextType))
        {
            return false;
        }

        var propertyName = kind == GeneratedRemoteRegistryKind.Hook ? "Hooks" : "Subscriptions";
        return serverType.GetMembers(propertyName).OfType<IPropertySymbol>()
            .Any(property => SymbolEqualityComparer.Default.Equals(property.Type, registryType));
    }

    private static bool RegistryOnShapeMatches(
        INamedTypeSymbol registryType,
        INamedTypeSymbol contextType,
        GeneratedRemoteRegistryKind kind)
    {
        var expectedOriginal = kind == GeneratedRemoteRegistryKind.Hook
            ? DotBoxDGenerationNames.TypeNames.RemoteHookPipelineWithContextOriginal
            : DotBoxDGenerationNames.TypeNames.RemoteSubscriptionPipelineWithContextOriginal;
        return registryType.GetMembers("On").OfType<IMethodSymbol>().Any(member =>
            member.Arity == 1 &&
            SymbolEqualityComparer.Default.Equals(member.ContainingType, registryType) &&
            member.Parameters.Length == 0 &&
            member.ReturnType is INamedTypeSymbol returnType &&
            string.Equals(returnType.OriginalDefinition.ToDisplayString(), expectedOriginal, StringComparison.Ordinal) &&
            returnType.TypeArguments.Length == 2 &&
            SymbolEqualityComparer.Default.Equals(returnType.TypeArguments[0], member.TypeParameters[0]) &&
            SymbolEqualityComparer.Default.Equals(returnType.TypeArguments[1], contextType));
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);

    private sealed record RegistryMarker(
        GeneratedRemoteRegistryKind Kind,
        INamedTypeSymbol ServerType,
        INamedTypeSymbol ContextType);

    private enum GeneratedRemoteRegistryKind
    {
        Hook,
        Subscription
    }
}
