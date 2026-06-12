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
    {
        var properties = eventType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && p.GetMethod is not null)
            .ToArray();

        properties = ConstructorPropertyOrder(eventType, properties) ?? properties;
        return properties
            .Select(p => new EventPropertyModel(p.Name, SandboxTypeName(p.Type)))
            .ToArray();
    }

    public static IReadOnlyList<LiveSettingModel> LiveSettings(
        INamedTypeSymbol kernelType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => kernelType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(IsLiveSetting)
            .Select(property => ToLiveSetting(property, semanticModel, cancellationToken))
            .ToArray();

    private static bool IsLiveSetting(IPropertySymbol property)
        => property.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(),
            "SafeIR.Plugins.LiveSettingAttribute",
            StringComparison.Ordinal));

    private static LiveSettingModel ToLiveSetting(
        IPropertySymbol property,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var syntax = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;
        var range = Range(property);
        return new LiveSettingModel(
            property.Name,
            SandboxTypeName(property.Type),
            LiteralReader.DefaultValue(property.Type, syntax?.Initializer?.Value, semanticModel, cancellationToken),
            range.Min,
            range.Max);
    }

    private static IPropertySymbol[]? ConstructorPropertyOrder(
        INamedTypeSymbol eventType,
        IPropertySymbol[] properties)
    {
        var byName = properties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var constructor in eventType.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public))
        {
            if (constructor.Parameters.Length == 0 || constructor.Parameters.Length != properties.Length)
            {
                continue;
            }

            var selected = new IPropertySymbol[constructor.Parameters.Length];
            var matched = true;
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                if (!byName.TryGetValue(parameter.Name, out var property) ||
                    !SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
                {
                    matched = false;
                    break;
                }

                selected[i] = property;
            }

            if (matched)
            {
                return selected;
            }
        }

        return null;
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
