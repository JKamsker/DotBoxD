using System.Collections.Concurrent;
using System.Reflection;

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

    private readonly ConcurrentDictionary<(Type Type, string Path), MemberInfo[]> _chains = new();

    /// <summary>Reads the value at <paramref name="path"/> from <paramref name="target"/>, or <see langword="null"/>.</summary>
    public object? Read(object target, string path)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var chain = _chains.GetOrAdd((target.GetType(), path), static key => ResolveChain(key.Type, key.Path));
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

    private static MemberInfo ResolveMember(Type type, string name, string path)
    {
        MemberInfo? member = type.GetProperty(name, MemberFlags);
        member ??= type.GetField(name, MemberFlags);
        return member ?? throw new InvalidOperationException(
            $"Event type '{type.FullName}' has no public instance member '{name}' for query path '{path}'.");
    }
}
