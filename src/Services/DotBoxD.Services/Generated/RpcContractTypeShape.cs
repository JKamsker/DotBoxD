using System.Reflection;

namespace DotBoxD.Services.Generated;

internal static class RpcContractTypeShape
{
    private const int MaxDepth = 32;

    public static string Describe(Type? type)
        => type is null ? string.Empty : Describe(type, [], 0);

    private static string Describe(Type type, HashSet<Type> path, int depth)
    {
        if (depth >= MaxDepth)
        {
            return $"depth-limit:{Name(type)}";
        }

        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            var suffix = type.IsByRef ? "&" : type.IsPointer ? "*" : $"[{new string(',', type.GetArrayRank() - 1)}]";
            return Describe(type.GetElementType()!, path, depth + 1) + suffix;
        }

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return $"nullable<{Describe(nullable, path, depth + 1)}>";
        }

        if (type.IsEnum)
        {
            var values = Enum.GetNames(type)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => $"{name}={EnumValue(type, name)}");
            return $"enum:{Name(type)}:{Name(Enum.GetUnderlyingType(type))}{{{string.Join(",", values)}}}";
        }

        if (IsScalar(type))
        {
            return Name(type);
        }

        if (type.IsGenericType)
        {
            var arguments = string.Join(",", type.GetGenericArguments().Select(argument => Describe(argument, path, depth + 1)));
            return $"{Name(type.GetGenericTypeDefinition())}<{arguments}>";
        }

        if (!path.Add(type))
        {
            return $"ref:{Name(type)}";
        }

        try
        {
            var members = PublicDataMembers(type)
                .Select(member => $"{member.Kind}:{member.Name}:{Describe(member.Type, path, depth + 1)}");
            return $"dto:{Name(type)}{{{string.Join(",", members)}}}";
        }
        finally
        {
            path.Remove(type);
        }
    }

    private static IEnumerable<DataMember> PublicDataMembers(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
            .Select(property => new DataMember("property", property.Name, property.PropertyType));
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => new DataMember("field", field.Name, field.FieldType));

        return properties.Concat(fields)
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Kind, StringComparer.Ordinal);
    }

    private static bool IsScalar(Type type)
        => type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
           type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) ||
           type == typeof(Guid) || type == typeof(CancellationToken) || type == typeof(void);

    private static string EnumValue(Type type, string name)
    {
        var value = Enum.Parse(type, name);
        var underlyingValue = Convert.ChangeType(
            value,
            Enum.GetUnderlyingType(type),
            System.Globalization.CultureInfo.InvariantCulture);
        return Convert.ToString(underlyingValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string Name(Type type)
        => type.FullName ?? type.Name;

    private sealed record DataMember(string Kind, string Name, Type Type);
}
