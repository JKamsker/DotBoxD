using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Reads dotted member paths (for example <c>AttackerId</c> or <c>Source.Id</c>) off runtime event objects
/// via cached reflection. The resolved property/field chain is cached per (runtime type, path); a
/// <see langword="null"/> anywhere along the chain short-circuits to <see langword="null"/>. The reader is
/// thread-safe and intended to be shared across a dispatcher.
/// </summary>
public sealed class MemberValueReader
{
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

    // A shared reader must not keep transient event types alive after their instances are gone.
    private readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, MemberInfo[]>> _chains = new();

    /// <summary>Reads the value at <paramref name="path"/> from <paramref name="target"/>, or <see langword="null"/>.</summary>
    public object? Read(object target, string path)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var type = target.GetType();
        var chain = _chains.GetValue(type, static _ => new(StringComparer.Ordinal)).GetOrAdd(
            path, static (currentPath, rootType) => ResolveChain(rootType, currentPath), type);
        object? current = target;
        foreach (var member in chain)
        {
            if (current is null)
            {
                return null;
            }

            try
            {
                current = member switch
                {
                    PropertyInfo property => property.GetValue(current),
                    FieldInfo field => field.GetValue(current),
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException)
            {
                // A getter that throws (reflection wraps it in TargetInvocationException) degrades to an
                // unreadable value (null) — consistent with the comparer's "incomparable -> false" contract
                // — so one event's faulty member cannot abort dispatch for other subscribers.
                return null;
            }
        }

        return current;
    }

    private static MemberInfo[] ResolveChain(Type rootType, string path)
    {
        var segments = path.Split('.');
        var chain = new MemberInfo[segments.Length];
        var current = rootType;
        for (var i = 0; i < segments.Length; i++)
        {
            var member = ResolveMember(current, segments[i], path);
            chain[i] = member;
            current = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => current,
            };
        }

        return chain;
    }

    internal static Type ResolvePathType(Type rootType, string path) =>
        ResolveChain(rootType, path)[^1] switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new InvalidOperationException($"Query path '{path}' is not a property or field."),
        };

    private static MemberInfo ResolveMember(Type type, string name, string path)
    {
        try
        {
            MemberInfo? member = ResolveProperty(type, name);
            if (member is PropertyInfo property &&
                (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0))
            {
                throw new InvalidOperationException(
                    $"Event property '{type.FullName}.{name}' must have a public parameterless getter for query path '{path}'.");
            }

            member ??= type.GetField(name, MemberFlags);
            return member ?? throw new InvalidOperationException(
                $"Event type '{type.FullName}' has no public instance member '{name}' for query path '{path}'.");
        }
        catch (AmbiguousMatchException ex)
        {
            throw new InvalidOperationException(
                $"Event type '{type.FullName}' has an ambiguous member '{name}' for query path '{path}'.", ex);
        }
    }

    private static PropertyInfo? ResolveProperty(Type type, string name)
    {
        var property = type.GetProperty(name, MemberFlags);
        if (property is not null || !type.IsInterface)
        {
            return property;
        }

        // Interface reflection does not include inherited properties. Keep the most-derived
        // declarations, deduplicate diamonds, and reject unrelated declarations as ambiguous.
        var inherited = type.GetInterfaces()
            .SelectMany(parent => parent.GetProperties(MemberFlags))
            .Where(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        var matches = inherited.Where(candidate => !inherited.Any(other =>
            other.DeclaringType!.GetInterfaces().Contains(candidate.DeclaringType!))).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new AmbiguousMatchException($"Interface '{type.FullName}' has ambiguous property '{name}'.")
        };
    }
}
