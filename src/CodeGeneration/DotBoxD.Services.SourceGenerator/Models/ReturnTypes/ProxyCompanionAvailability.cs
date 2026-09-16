using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ProxyCompanionAvailability
{
    internal static bool HasErrorObsoleteAttribute(ISymbol symbol, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.ToDisplayString() != "System.ObsoleteAttribute" ||
                !IsFrameworkAssembly(attributeClass.ContainingAssembly.Name))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 1 &&
                attribute.ConstructorArguments[1].Value is bool isError &&
                isError)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsExperimental(ISymbol symbol, CancellationToken ct) =>
        HasTrustedAttribute(symbol, "System.Diagnostics.CodeAnalysis.ExperimentalAttribute", ct);

    internal static bool RequiresAssemblyFiles(ISymbol symbol, CancellationToken ct) =>
        HasTrustedAttribute(symbol, "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute", ct);

    internal static bool RequiresPreviewFeatures(ISymbol symbol, CancellationToken ct) =>
        HasTrustedAttribute(symbol, "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute", ct);

    internal static bool IsPlatformRestricted(ISymbol symbol, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                ct.ThrowIfCancellationRequested();

                if (type.ToDisplayString() == "System.Runtime.Versioning.OSPlatformAttribute" &&
                    ReturnTypeClassifier.IsTrustedFrameworkType(type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasTrustedAttribute(ISymbol symbol, string metadataName, CancellationToken ct)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attribute.AttributeClass is { } attributeType &&
                attributeType.ToDisplayString() == metadataName &&
                ReturnTypeClassifier.IsTrustedFrameworkType(attributeType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkAssembly(string assemblyName) =>
        assemblyName is "mscorlib" or "netstandard" or "System.Private.CoreLib" or "System.Runtime";
}
