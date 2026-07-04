using System.Reflection;
using DotBoxD.Plugins.Runtime.Rpc;
using Expr = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Indexing;

internal static class EventIndexPathResolver
{
    private const int MaxDepth = 8;

    public static IReadOnlyList<EventIndexKeyPath<TEvent>> IndexKeyPaths<TEvent>()
    {
        var keys = new List<EventIndexKeyPath<TEvent>>();
        CollectIndexKeys(
            typeof(TEvent),
            prefix: string.Empty,
            chain: [],
            keys,
            active: new HashSet<Type>(),
            depth: 0);
        return keys;
    }

    public static bool TryResolveDottedPath(
        Type eventType,
        string rootName,
        IReadOnlyList<int> fieldIndexes,
        out string path)
    {
        path = string.Empty;
        if (!TryEventPropertyType(eventType, rootName, out var currentType))
        {
            return false;
        }

        if (fieldIndexes.Count == 0)
        {
            path = rootName;
            return true;
        }

        var parts = new List<string>(fieldIndexes.Count + 1) { rootName };
        for (var i = 0; i < fieldIndexes.Count; i++)
        {
            var fields = RecordMembers(currentType);
            var index = fieldIndexes[i];
            if (index < 0 || index >= fields.Count)
            {
                return false;
            }

            var field = fields[index];
            parts.Add(field.Name);
            currentType = field.Type;
        }

        path = string.Join('.', parts);
        return true;
    }

    private static void CollectIndexKeys<TEvent>(
        Type type,
        string prefix,
        List<PropertyInfo> chain,
        List<EventIndexKeyPath<TEvent>> keys,
        HashSet<Type> active,
        int depth)
    {
        if (depth > MaxDepth || !active.Add(type))
        {
            return;
        }

        foreach (var property in ReadableProperties(type))
        {
            var path = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
            chain.Add(property);
            if (property.GetCustomAttribute<EventIndexKeyAttribute>() is not null)
            {
                keys.Add(new EventIndexKeyPath<TEvent>(
                    path,
                    Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType,
                    CompileGetter<TEvent>(chain)));
            }

            if (IsRecordType(property.PropertyType))
            {
                CollectIndexKeys(property.PropertyType, path, chain, keys, active, depth + 1);
            }

            chain.RemoveAt(chain.Count - 1);
        }

        active.Remove(type);
    }

    private static Func<TEvent, object?> CompileGetter<TEvent>(IReadOnlyList<PropertyInfo> chain)
    {
        var parameter = Expr.Parameter(typeof(TEvent), "value");
        var returned = Expr.Label(typeof(object));
        var variables = new List<System.Linq.Expressions.ParameterExpression>();
        var expressions = new List<Expr>();
        Expr current = parameter;
        for (var i = 0; i < chain.Count; i++)
        {
            if (CanBeNull(current.Type))
            {
                var temp = Expr.Variable(current.Type, "v" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                variables.Add(temp);
                expressions.Add(Expr.Assign(temp, current));
                expressions.Add(Expr.IfThen(
                    Expr.Equal(temp, Expr.Constant(null, current.Type)),
                    Expr.Return(returned, Expr.Constant(null, typeof(object)))));
                current = temp;
            }

            current = Expr.Property(current, chain[i]);
        }

        expressions.Add(Expr.Label(returned, Expr.Convert(current, typeof(object))));
        return Expr.Lambda<Func<TEvent, object?>>(Expr.Block(variables, expressions), parameter).Compile();
    }

    private static bool TryEventPropertyType(Type eventType, string name, out Type propertyType)
    {
        foreach (var property in ReadableProperties(eventType))
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                propertyType = property.PropertyType;
                return true;
            }
        }

        propertyType = typeof(object);
        return false;
    }

    private static IReadOnlyList<PropertyInfo> ReadableProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property =>
                property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                !KernelRpcMarshaller.IsIgnoredMember(property))
            .ToArray();

    private static IReadOnlyList<RecordField> RecordMembers(Type type)
    {
        var fields = new List<RecordField>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var properties = type.GetProperties(flags);
        Array.Sort(properties, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
        foreach (var property in properties)
        {
            if (property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                !KernelRpcMarshaller.IsIgnoredMember(property))
            {
                fields.Add(new RecordField(property.Name, property.PropertyType));
            }
        }

        var publicFields = type.GetFields(flags);
        Array.Sort(publicFields, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
        foreach (var field in publicFields)
        {
            if (!field.IsLiteral && !KernelRpcMarshaller.IsIgnoredMember(field))
            {
                fields.Add(new RecordField(field.Name, field.FieldType));
            }
        }

        return fields;
    }

    private static bool IsRecordType(Type type)
    {
        try
        {
            return KernelRpcMarshaller.SandboxTypeOf(type).IsRecord;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool CanBeNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private readonly record struct RecordField(string Name, Type Type);
}

internal sealed record EventIndexKeyPath<TEvent>(
    string Path,
    Type Type,
    Func<TEvent, object?> Getter);
