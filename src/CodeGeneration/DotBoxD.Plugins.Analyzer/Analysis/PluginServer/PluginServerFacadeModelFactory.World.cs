using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol type)
    {
        foreach (var candidate in type.Interfaces)
        {
            if (HasAttribute(candidate, DotBoxDMetadataNames.DotBoxDServiceAttribute))
            {
                return candidate;
            }
        }

        return null;
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

        var worldNamespace = worldType.ContainingNamespace.ToDisplayString();
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

            foreach (var parameter in method.Parameters)
            {
                if (string.Equals(parameter.Name, "updates", StringComparison.Ordinal) &&
                    parameter.Type is IArrayTypeSymbol updateArray)
                {
                    return updateArray.ElementType;
                }
            }
        }

        return null;
    }
}
