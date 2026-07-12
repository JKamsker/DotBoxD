using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

internal static class RecordMemberDiscovery
{
    private const BindingFlags DeclaredPublicInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<MemberInfo> Discover(Type type)
    {
        var hierarchy = Hierarchy(type);
        var members = new List<MemberInfo>();
        AddProperties(type, hierarchy, members);
        AddFields(type, hierarchy, members);
        return members;
    }

    private static IReadOnlyList<Type> Hierarchy(Type type)
    {
        var hierarchy = new List<Type>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current != typeof(object) && current != typeof(ValueType))
            {
                hierarchy.Add(current);
            }
        }

        hierarchy.Reverse();
        return hierarchy;
    }

    private static void AddProperties(
        Type type,
        IReadOnlyList<Type> hierarchy,
        List<MemberInfo> members)
    {
        foreach (var declaringType in hierarchy)
        {
            var properties = declaringType.GetProperties(DeclaredPublicInstance);
            Array.Sort(properties, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var property in properties)
            {
                AddProperty(type, members, property);
            }
        }
    }

    private static void AddProperty(Type type, List<MemberInfo> members, PropertyInfo property)
    {
        if (property.GetMethod is not { IsPublic: true } getter ||
            property.GetIndexParameters().Length != 0 ||
            string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
        {
            return;
        }

        var overriddenIndex = OverriddenPropertyIndex(members, getter);
        if (KernelRpcMarshaller.IsIgnoredMember(property))
        {
            if (overriddenIndex >= 0)
            {
                members.RemoveAt(overriddenIndex);
            }

            return;
        }

        if (overriddenIndex >= 0)
        {
            members[overriddenIndex] = property;
            return;
        }

        RejectDuplicate(type, members, property.Name);
        members.Add(property);
    }

    private static int OverriddenPropertyIndex(IReadOnlyList<MemberInfo> members, MethodInfo getter)
    {
        var baseDefinition = getter.GetBaseDefinition();
        if (baseDefinition == getter)
        {
            return -1;
        }

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is PropertyInfo { GetMethod: { } existingGetter } &&
                existingGetter.GetBaseDefinition() == baseDefinition)
            {
                return i;
            }
        }

        return -1;
    }

    private static void AddFields(Type type, IReadOnlyList<Type> hierarchy, List<MemberInfo> members)
    {
        foreach (var declaringType in hierarchy)
        {
            var fields = declaringType.GetFields(DeclaredPublicInstance);
            Array.Sort(fields, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var field in fields)
            {
                if (!field.IsLiteral && !KernelRpcMarshaller.IsIgnoredMember(field))
                {
                    RejectDuplicate(type, members, field.Name);
                    members.Add(field);
                }
            }
        }
    }

    private static void RejectDuplicate(Type type, IReadOnlyList<MemberInfo> members, string name)
    {
        foreach (var member in members)
        {
            if (string.Equals(member.Name, name, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{type}' has multiple public data members named '{name}' " +
                    "across its inheritance hierarchy; hide or ignore one member explicitly.");
            }
        }
    }
}
