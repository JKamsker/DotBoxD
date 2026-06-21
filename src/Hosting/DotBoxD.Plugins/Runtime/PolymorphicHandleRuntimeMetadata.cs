using System.Reflection;

namespace DotBoxD.Plugins.Runtime;

internal sealed record PolymorphicHandleRuntimeMetadata(
    Type HandleType,
    Type KeyType,
    PropertyInfo? KeyProperty,
    FieldInfo? KeyField);

internal static class PolymorphicHandleRuntimeMetadataReader
{
    public static bool TryResolve(Type type, out PolymorphicHandleRuntimeMetadata metadata)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            var attribute = current.GetCustomAttribute<PolymorphicHandleAttribute>(inherit: false);
            if (attribute is null)
            {
                continue;
            }

            if (KeyMember(current, attribute.KeyMember) is { } key)
            {
                metadata = key;
                return true;
            }
        }

        metadata = null!;
        return false;
    }

    private static PolymorphicHandleRuntimeMetadata? KeyMember(Type handleType, string keyMember)
    {
        for (var current = handleType; current is not null && current != typeof(object); current = current.BaseType)
        {
            var property = current.GetProperty(
                keyMember,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (property is not null)
            {
                return new PolymorphicHandleRuntimeMetadata(handleType, property.PropertyType, property, null);
            }

            var field = current.GetField(
                keyMember,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field is not null)
            {
                return new PolymorphicHandleRuntimeMetadata(handleType, field.FieldType, null, field);
            }
        }

        return null;
    }
}
