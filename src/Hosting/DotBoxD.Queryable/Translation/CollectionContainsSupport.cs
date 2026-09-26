using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace DotBoxD.Queryable.Translation;

internal static class CollectionContainsSupport
{
    public static void Validate(MethodCallExpression call, object collection)
    {
        try
        {
            ValidateImplementation(call, collection);
        }
        catch (Exception error) when (error is not QueryTranslationException and not OperationCanceledException)
        {
            throw new QueryTranslationException(
                $"Could not resolve Contains membership for '{call}'; use ToArray() when enumeration membership is intended.",
                error);
        }
    }

    private static void ValidateImplementation(MethodCallExpression call, object collection)
    {
        var method = MembershipMethod(call, collection);
        HashSet<object>? visited = null;
        while (method is not null)
        {
            var implementation = ResolveImplementation(method, collection.GetType());
            if (!IsFrameworkImplementation(implementation))
            {
                throw QueryTranslationException.Unsupported(call,
                    "custom Contains implementations cannot be represented as portable membership; use ToArray() when enumeration membership is intended.");
            }

            var inner = CollectionWrapperReader.Read(collection, implementation.DeclaringType!);
            if (inner is null)
            {
                return;
            }

            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(collection))
            {
                throw QueryTranslationException.Unsupported(call, "cyclic collection wrappers cannot define portable Contains membership.");
            }

            collection = inner;
            method = CollectionContainsMethod(method.GetParameters()[0].ParameterType);
        }
    }

    private static MethodInfo? MembershipMethod(MethodCallExpression call, object collection)
    {
        if (call.Object is not null)
        {
            return call.Method;
        }

        if (call.Method.DeclaringType == typeof(Enumerable))
        {
            // Enumerable.Contains delegates to ICollection<T>.Contains when that interface is available.
            var method = CollectionContainsMethod(call.Method.GetGenericArguments()[0]);
            return method.DeclaringType!.IsInstanceOfType(collection) ? method : null;
        }

        // MemoryExtensions operates on the captured span contents, without collection dispatch.
        return null;
    }

    private static MethodInfo CollectionContainsMethod(Type elementType) =>
        typeof(ICollection<>).MakeGenericType(elementType).GetMethod(nameof(ICollection<int>.Contains))!;

    private static MethodInfo ResolveImplementation(MethodInfo method, Type type)
    {
        if (method.DeclaringType!.IsInterface)
        {
            // Arrays have runtime-provided generic interfaces that GetInterfaceMap does not expose.
            if (type.IsArray)
            {
                return method;
            }

            var map = type.GetInterfaceMap(method.DeclaringType);
            var index = Array.IndexOf(map.InterfaceMethods, method);
            method = map.TargetMethods[index];
        }

        if (!method.IsVirtual || method.IsFinal)
        {
            return method;
        }

        var definition = method.GetBaseDefinition();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMethods(flags))
            {
                if (candidate.IsVirtual && candidate.GetBaseDefinition() == definition)
                {
                    return candidate;
                }
            }
        }

        return method;
    }

    private static bool IsFrameworkImplementation(MethodInfo method) => IsFrameworkType(method.DeclaringType!);

    public static bool IsFrameworkType(Type type)
    {
        var assembly = type.Assembly;
        return assembly == typeof(List<>).Assembly || assembly == typeof(SortedSet<>).Assembly ||
            assembly == typeof(ImmutableArray<>).Assembly || assembly == typeof(Enumerable).Assembly;
    }
}
