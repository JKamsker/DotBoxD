using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol type)
    {
        INamedTypeSymbol? worldType = null;
        foreach (var candidate in type.Interfaces)
        {
            if (!HasAttribute(candidate, DotBoxDMetadataNames.RpcServiceAttribute))
            {
                continue;
            }

            if (worldType is not null)
            {
                throw new NotSupportedException(
                    $"Generated plugin server '{type.Name}' must directly implement one [RpcService] world interface.");
            }

            worldType = candidate;
        }

        return worldType;
    }

    private static void ValidateWorldType(
        INamedTypeSymbol serverType,
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        if (worldType.TypeKind != TypeKind.Error &&
            !worldType.IsFileLocal &&
            IsAccessibleFromGeneratedServer(compilation, serverType, worldType))
        {
            return;
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' world interface '{worldType.ToDisplayString()}' " +
            "is file-local or inaccessible and cannot be named from the generated facade.");
    }

    internal static bool IsAccessibleFromGeneratedServer(
        Compilation compilation,
        INamedTypeSymbol serverType,
        ISymbol symbol)
        => compilation.IsSymbolAccessibleWithin(symbol, serverType) &&
           !CrossesUnfriendlyInternalBoundary(symbol, serverType.ContainingAssembly);

    private static bool CrossesUnfriendlyInternalBoundary(ISymbol symbol, IAssemblySymbol generatedAssembly)
    {
        if (IsUnfriendlyInternal(symbol, generatedAssembly))
        {
            return true;
        }

        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (IsUnfriendlyInternal(type, generatedAssembly))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnfriendlyInternal(ISymbol symbol, IAssemblySymbol generatedAssembly)
        => symbol.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal &&
           symbol.ContainingAssembly is { } assembly &&
           !SymbolEqualityComparer.Default.Equals(assembly, generatedAssembly) &&
           !assembly.GivesAccessTo(generatedAssembly);

    private static void ValidateControlServiceAccessibility(
        INamedTypeSymbol serverType,
        Compilation compilation,
        INamedTypeSymbol controlServiceType)
    {
        if (controlServiceType.TypeKind != TypeKind.Error &&
            !controlServiceType.IsFileLocal &&
            IsAccessibleFromGeneratedServer(compilation, serverType, controlServiceType))
        {
            return;
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' control-plane contract '{controlServiceType.ToDisplayString()}' " +
            "is file-local or inaccessible and cannot be named from the generated facade.");
    }

    private static INamedTypeSymbol? ResolveControlService(
        INamedTypeSymbol serverType,
        Compilation compilation,
        INamedTypeSymbol worldType)
    {
        var attribute = GeneratePluginServerAttribute(serverType);
        var explicitControlService = attribute is null ? null : ControlServiceType(attribute);
        if (explicitControlService is not null)
        {
            return explicitControlService;
        }

        var worldNamespace = PluginServerFacadeNameFormatter.NamespaceMetadataName(worldType.ContainingNamespace);
        return compilation.GetTypeByMetadataName(worldNamespace + ".Ipc.IGamePluginControlService");
    }

    private static INamedTypeSymbol? ControlServiceType(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (!string.Equals(argument.Key, "ControlService", StringComparison.Ordinal))
            {
                continue;
            }

            if (argument.Value.Value is INamedTypeSymbol controlServiceType)
            {
                return controlServiceType;
            }

            if (argument.Value.Value is null)
            {
                // Explicit `ControlService = null` is equivalent to omitting it: fall back to the convention.
                return null;
            }

            throw new NotSupportedException("ControlService must be typeof(TControlService).");
        }

        return null;
    }

    private static ITypeSymbol? ResolveLiveSettingUpdateType(
        INamedTypeSymbol controlServiceType,
        CancellationToken cancellationToken)
    {
        foreach (var member in MembersIncludingInherited(controlServiceType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: false,
                    Name: "UpdateSettingsAsync"
                } method)
            {
                continue;
            }

            if (LiveSettingUpdateElementType(method) is { } elementType)
            {
                return elementType;
            }
        }

        return null;
    }

    // The live-setting update element type is the element type of UpdateSettingsAsync's update-batch array.
    // The conventional `updates` parameter wins when present; otherwise the method's single array parameter is
    // used, so an explicit control-plane contract is not forced to use a specific parameter name.
    private static ITypeSymbol? LiveSettingUpdateElementType(IMethodSymbol method)
    {
        IArrayTypeSymbol? fallback = null;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not IArrayTypeSymbol updateArray)
            {
                continue;
            }

            if (string.Equals(parameter.Name, "updates", StringComparison.Ordinal))
            {
                return updateArray.ElementType;
            }

            fallback ??= updateArray;
        }

        return fallback?.ElementType;
    }

    private static bool AssemblyEnablesClsCompliance(Compilation compilation)
        => compilation.Assembly.GetAttributes().Any(static attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.CLSCompliantAttribute", StringComparison.Ordinal) &&
            attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is true);
}
