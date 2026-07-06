using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerControlContractValidator
{
    public static void Validate(
        INamedTypeSymbol serverType,
        Compilation compilation,
        INamedTypeSymbol controlServiceType,
        ITypeSymbol liveSettingUpdateType)
    {
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        var cancellationTokenType = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        var valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        var wireClientType = compilation.GetTypeByMetadataName("DotBoxD.Abstractions.IServerExtensionWireClient");
        if (cancellationTokenType is null ||
            valueTaskType is null ||
            valueTaskOfT is null ||
            wireClientType is null)
        {
            return;
        }

        EnsureWireClientContract(serverType, controlServiceType, wireClientType);
        var valueTaskString = valueTaskOfT.Construct(stringType);
        ValidateLiveSettingUpdateConstructor(serverType, compilation, liveSettingUpdateType, stringType);
        EnsureControlMethod(serverType, controlServiceType, "InstallPluginAsync", valueTaskString, [stringType, cancellationTokenType]);
        EnsureControlMethod(serverType, controlServiceType, "InstallSubscriptionAsync", valueTaskString, [stringType, cancellationTokenType]);
        EnsureControlMethod(serverType, controlServiceType, "InstallServerExtensionAsync", valueTaskString, [stringType, cancellationTokenType]);
        EnsureControlMethod(
            serverType,
            controlServiceType,
            "UpdateSettingsAsync",
            valueTaskType,
            [stringType, compilation.CreateArrayTypeSymbol(liveSettingUpdateType), boolType, cancellationTokenType]);
        EnsureControlMethod(serverType, controlServiceType, "HoldUntilShutdownAsync", valueTaskType, [cancellationTokenType]);
    }

    private static void EnsureWireClientContract(
        INamedTypeSymbol serverType,
        INamedTypeSymbol controlServiceType,
        INamedTypeSymbol wireClientType)
    {
        if (SymbolEqualityComparer.Default.Equals(controlServiceType, wireClientType) ||
            controlServiceType.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, wireClientType)))
        {
            return;
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' control-plane contract '{controlServiceType.ToDisplayString()}' must implement DotBoxD.Abstractions.IServerExtensionWireClient.");
    }

    private static void EnsureControlMethod(
        INamedTypeSymbol serverType,
        INamedTypeSymbol controlServiceType,
        string name,
        ITypeSymbol returnType,
        IReadOnlyList<ITypeSymbol> parameterTypes)
    {
        foreach (var member in PluginServerFacadeModelFactory.MembersIncludingInherited(controlServiceType))
        {
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: false
                } method &&
                string.Equals(method.Name, name, StringComparison.Ordinal) &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType) &&
                ParametersMatch(method.Parameters, parameterTypes))
            {
                return;
            }
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' control-plane contract '{controlServiceType.ToDisplayString()}' must declare {name} with the generated facade signature.");
    }

    private static bool ParametersMatch(
        IReadOnlyList<IParameterSymbol> actual,
        IReadOnlyList<ITypeSymbol> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < actual.Count; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(actual[i].Type, expected[i]) ||
                actual[i].RefKind != RefKind.None)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateLiveSettingUpdateConstructor(
        INamedTypeSymbol serverType,
        Compilation compilation,
        ITypeSymbol liveSettingUpdateType,
        ITypeSymbol stringType)
    {
        var named = RequireLiveSettingUpdateType(serverType, compilation, liveSettingUpdateType);
        if (named.InstanceConstructors.Any(constructor =>
                LiveSettingUpdateConstructorMatches(compilation, serverType, constructor, stringType)))
        {
            return;
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' live-setting update type '{liveSettingUpdateType.ToDisplayString()}' must expose an accessible constructor '(string name, string value)'.");
    }

    private static INamedTypeSymbol RequireLiveSettingUpdateType(
        INamedTypeSymbol serverType,
        Compilation compilation,
        ITypeSymbol liveSettingUpdateType)
    {
        if (liveSettingUpdateType is not INamedTypeSymbol named)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' live-setting update type '{liveSettingUpdateType.ToDisplayString()}' must be a named type.");
        }

        if (named.TypeKind != TypeKind.Error &&
            !named.IsFileLocal &&
            PluginServerFacadeModelFactory.IsAccessibleFromGeneratedServer(compilation, serverType, named))
        {
            return named;
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' live-setting update type '{liveSettingUpdateType.ToDisplayString()}' must be accessible from the generated facade.");
    }

    private static bool LiveSettingUpdateConstructorMatches(
        Compilation compilation,
        INamedTypeSymbol serverType,
        IMethodSymbol constructor,
        ITypeSymbol stringType)
        => constructor.Parameters.Length == 2 &&
           ConstructorParameterMatches(constructor.Parameters[0], stringType) &&
           ConstructorParameterMatches(constructor.Parameters[1], stringType) &&
           PluginServerFacadeModelFactory.IsAccessibleFromGeneratedServer(compilation, serverType, constructor);

    private static bool ConstructorParameterMatches(IParameterSymbol parameter, ITypeSymbol expectedType)
        => parameter.RefKind == RefKind.None &&
           SymbolEqualityComparer.Default.Equals(parameter.Type, expectedType);
}
