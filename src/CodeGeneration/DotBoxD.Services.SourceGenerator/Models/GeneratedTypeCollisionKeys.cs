using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class GeneratedTypeCollisionKeys
{
    public static PrimaryGeneratedTypeKeys Primary(ServiceModel model)
    {
        var serviceName = NamingHelpers.StripInterfacePrefix(model.InterfaceName);
        return new PrimaryGeneratedTypeKeys(
            new ExistingTypeKey(model.Namespace, serviceName + "Proxy", 0),
            new ExistingTypeKey(model.Namespace, serviceName + "Dispatcher", 0));
    }

    public static ExistingTypeKey AsyncSibling(ServiceModel model) =>
        new(model.Namespace, NamingHelpers.AsyncSiblingInterfaceName(model.InterfaceName), 0);

    public static ExistingTypeKey Extensions { get; } = new(
        ServicesGeneratorTypeNames.GeneratedNamespace,
        ServicesGeneratorTypeNames.GeneratedExtensionsType,
        0);

    public static ExistingTypeKey Factory { get; } = new(
        ServicesGeneratorTypeNames.GeneratedNamespace,
        ServicesGeneratorTypeNames.GeneratedFactoryType,
        0);
}

internal readonly record struct PrimaryGeneratedTypeKeys(
    ExistingTypeKey Proxy,
    ExistingTypeKey Dispatcher);
