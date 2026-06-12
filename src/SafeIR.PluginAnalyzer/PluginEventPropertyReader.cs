namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal static class PluginEventPropertyReader
{
    public static IPropertySymbol[] Read(INamedTypeSymbol eventType)
    {
        var properties = ReadableProperties(eventType).ToArray();
        ValidatePropertyNames(properties);
        return ConstructorPropertyOrder(eventType, properties) ?? properties;
    }

    private static void ValidatePropertyNames(IPropertySymbol[] properties)
    {
        var duplicate = properties
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new NotSupportedException(
                $"Event property '{duplicate.First().Name}' is declared more than once or differs only by case.");
        }
    }

    private static IEnumerable<IPropertySymbol> ReadableProperties(INamedTypeSymbol eventType)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = eventType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        while (hierarchy.Count > 0)
        {
            var current = hierarchy.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property && IsReadableProperty(property))
                {
                    yield return property;
                }
            }
        }
    }

    private static bool IsReadableProperty(IPropertySymbol property)
        => property.DeclaredAccessibility == Accessibility.Public &&
           !property.IsStatic &&
           property.GetMethod is not null &&
           property.Parameters.Length == 0;

    private static IPropertySymbol[]? ConstructorPropertyOrder(
        INamedTypeSymbol eventType,
        IPropertySymbol[] properties)
    {
        foreach (var constructor in eventType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length == 0 || constructor.Parameters.Length != properties.Length)
            {
                continue;
            }

            if (MatchesDeclaredPropertyOrder(constructor, properties))
            {
                return properties;
            }

            if (ReorderedConstructorProperties(constructor, properties) is { } reordered)
            {
                return reordered;
            }
        }

        return null;
    }

    private static bool MatchesDeclaredPropertyOrder(IMethodSymbol constructor, IPropertySymbol[] properties)
    {
        for (var i = 0; i < properties.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            var property = properties[i];
            if (!NameMatches(parameter.Name, property.Name) ||
                !SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static IPropertySymbol[]? ReorderedConstructorProperties(
        IMethodSymbol constructor,
        IPropertySymbol[] properties)
    {
        var selected = new IPropertySymbol[properties.Length];
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            var property = FindProperty(properties, parameter);
            if (property is null)
            {
                return null;
            }

            selected[i] = property;
        }

        return selected;
    }

    private static IPropertySymbol? FindProperty(IPropertySymbol[] properties, IParameterSymbol parameter)
    {
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (NameMatches(parameter.Name, property.Name) &&
                SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
            {
                return property;
            }
        }

        return null;
    }

    private static bool NameMatches(string parameterName, string propertyName)
        => string.Equals(parameterName, propertyName, StringComparison.OrdinalIgnoreCase);
}
