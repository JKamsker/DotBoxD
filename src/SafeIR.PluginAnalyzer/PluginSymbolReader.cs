namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class PluginSymbolReader
{
    public static string? PluginId(IReadOnlyList<AttributeData> attributes)
        => attributes.FirstOrDefault(a => string.Equals(
                a.AttributeClass?.ToDisplayString(),
                "SafeIR.Plugins.GamePluginAttribute",
                StringComparison.Ordinal))
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    public static INamedTypeSymbol? EventType(INamedTypeSymbol kernelType)
        => kernelType.AllInterfaces
            .FirstOrDefault(i => string.Equals(
                i.OriginalDefinition.ToDisplayString(),
                "SafeIR.Plugins.IEventKernel<TEvent>",
                StringComparison.Ordinal))
            ?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;

    public static IReadOnlyList<EventPropertyModel> EventProperties(INamedTypeSymbol eventType)
        => eventType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && p.GetMethod is not null)
            .Select(p => new EventPropertyModel(p.Name, SandboxTypeName(p.Type)))
            .ToArray();

    public static IReadOnlyList<LiveSettingModel> LiveSettings(INamedTypeSymbol kernelType)
        => kernelType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(IsLiveSetting)
            .Select(ToLiveSetting)
            .ToArray();

    private static bool IsLiveSetting(IPropertySymbol property)
        => property.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            "SafeIR.Plugins.LiveSettingAttribute",
            StringComparison.Ordinal));

    private static LiveSettingModel ToLiveSetting(IPropertySymbol property)
    {
        var syntax = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;
        var range = Range(property);
        return new LiveSettingModel(
            property.Name,
            SandboxTypeName(property.Type),
            LiteralReader.DefaultValue(property.Type, syntax?.Initializer?.Value),
            range.Min,
            range.Max);
    }

    private static (string? Min, string? Max) Range(IPropertySymbol property)
    {
        var range = property.GetAttributes().FirstOrDefault(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            "System.ComponentModel.DataAnnotations.RangeAttribute",
            StringComparison.Ordinal));
        if (range is null || range.ConstructorArguments.Length < 2) {
            return (null, null);
        }

        return (
            LiteralReader.ObjectLiteral(range.ConstructorArguments[0].Value),
            LiteralReader.ObjectLiteral(range.ConstructorArguments[1].Value));
    }

    private static string SandboxTypeName(ITypeSymbol type)
        => type.SpecialType switch {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Int64 => "long",
            SpecialType.System_Double => "double",
            SpecialType.System_String => "string",
            _ => "unsupported"
        };
}
