using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class CallerInfoAttributeIdentity
{
    private static readonly IReadOnlyDictionary<string, string> AttributeKeys =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["System.Runtime.CompilerServices.CallerMemberNameAttribute"] = "member",
            ["System.Runtime.CompilerServices.CallerFilePathAttribute"] = "file",
            ["System.Runtime.CompilerServices.CallerLineNumberAttribute"] = "line",
            ["System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"] = "argument",
        };

    public static IReadOnlyDictionary<INamedTypeSymbol, string> Resolve(Compilation compilation)
    {
        var resolved = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var pair in AttributeKeys)
        {
            var symbol = compilation.GetTypesByMetadataName(pair.Key).FirstOrDefault(IsFrameworkType);
            if (symbol is not null)
            {
                resolved.Add(symbol, pair.Value);
            }
        }

        return resolved;
    }

    public static string GetKey(
        IParameterSymbol parameter,
        IReadOnlyDictionary<INamedTypeSymbol, string> resolved,
        CancellationToken ct)
    {
        var attributes = new List<string>();
        foreach (var attribute in parameter.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attribute.AttributeClass is not { } attributeClass ||
                !resolved.TryGetValue(attributeClass, out var key))
            {
                continue;
            }

            attributes.Add(key == "argument"
                ? key + ":" + GetCallerArgumentExpressionTarget(attribute)
                : key);
        }

        attributes.Sort(System.StringComparer.Ordinal);
        return string.Join("|", attributes);
    }

    private static bool IsFrameworkType(INamedTypeSymbol type) =>
        type.ContainingAssembly?.Name is
            "System.Runtime" or
            "System.Private.CoreLib" or
            "mscorlib" or
            "netstandard";

    private static string GetCallerArgumentExpressionTarget(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 1 &&
        attribute.ConstructorArguments[0].Value is string target
            ? target
            : string.Empty;
}
