using System.Collections.Concurrent;
using System.Reflection;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>
/// The host's compiled view of a subscription's <see cref="IndexedPredicate"/> metadata — the
/// "compile the metadata into whatever dispatch/index structure is natural for the runtime" half of
/// issue #47. It keeps only the predicates whose <see cref="IndexedPredicate.Path"/> is an
/// <see cref="EventIndexKeyAttribute"/> property of <typeparamref name="TEvent"/> (the fields this host
/// actually indexes) and evaluates them cheaply against an event with no sandbox entry.
/// <para>
/// Because every kept predicate is a <i>necessary</i> AND condition of the real predicate,
/// <see cref="CouldMatch"/> returning <c>false</c> is always a safe reject; returning <c>true</c> means
/// the event passed the cheap index and the host should still run the verified IR unless the manifest
/// reported full coverage.
/// </para>
/// </summary>
public sealed class EventIndexMatcher<TEvent>
{
    private static readonly IReadOnlyDictionary<string, PropertyInfo> IndexKeys = BuildIndexKeys();

    private readonly IReadOnlyList<IndexCheck> _checks;

    private EventIndexMatcher(IReadOnlyList<IndexCheck> checks, IReadOnlyList<IndexedPredicate> honored)
    {
        _checks = checks;
        HonoredPredicates = honored;
    }

    /// <summary>The manifest predicates this host can actually serve from an index (path is an index key).</summary>
    public IReadOnlyList<IndexedPredicate> HonoredPredicates { get; }

    /// <summary><c>true</c> when at least one manifest predicate mapped onto an indexed field.</summary>
    public bool HasIndex => _checks.Count > 0;

    /// <summary>The <see cref="EventIndexKeyAttribute"/> property names of <typeparamref name="TEvent"/>.</summary>
    public static IReadOnlyCollection<string> IndexKeyNames => (IReadOnlyCollection<string>)IndexKeys.Keys;

    public static EventIndexMatcher<TEvent> Create(IReadOnlyList<IndexedPredicate> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var checks = new List<IndexCheck>();
        var honored = new List<IndexedPredicate>();
        foreach (var predicate in predicates)
        {
            if (IndexKeys.TryGetValue(predicate.Path, out var property))
            {
                checks.Add(new IndexCheck(property, predicate.Operator, predicate.Value));
                honored.Add(predicate);
            }
        }

        return new EventIndexMatcher<TEvent>(checks, honored);
    }

    /// <summary>
    /// Evaluates the cheap index checks against <paramref name="value"/>. Returns <c>false</c> as soon as
    /// any indexed constraint is violated, so the host can skip dispatch entirely.
    /// </summary>
    public bool CouldMatch(TEvent value)
    {
        foreach (var check in _checks)
        {
            if (!check.Evaluate(value))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<string, PropertyInfo> BuildIndexKeys()
    {
        var keys = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in typeof(TEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<EventIndexKeyAttribute>() is not null && property.CanRead)
            {
                keys[property.Name] = property;
            }
        }

        return keys;
    }

    private sealed class IndexCheck(PropertyInfo property, IndexPredicateOperator op, object? value)
    {
        public bool Evaluate(TEvent target)
        {
            var actual = property.GetValue(target);
            return op switch
            {
                IndexPredicateOperator.Equals => Equals(actual, value),
                IndexPredicateOperator.NotEquals => !Equals(actual, value),
                IndexPredicateOperator.GreaterThan => Compare(actual, value) > 0,
                IndexPredicateOperator.GreaterThanOrEqual => Compare(actual, value) >= 0,
                IndexPredicateOperator.LessThan => Compare(actual, value) < 0,
                IndexPredicateOperator.LessThanOrEqual => Compare(actual, value) <= 0,
                _ => false,
            };
        }

        private static int Compare(object? actual, object? expected)
        {
            if (actual is IComparable comparable && expected is not null)
            {
                return comparable.CompareTo(expected);
            }

            // Ordering against null (or a non-comparable) cannot be decided from the index; treat as a
            // non-match so the verified IR remains the authority.
            return actual is null && expected is null ? 0 : 1;
        }
    }
}

/// <summary>
/// Per-event-type registry of cached <see cref="EventIndexMatcher{TEvent}"/> property maps. Pure helper so
/// callers that work with <see cref="Type"/> rather than a generic parameter can still reach the cache.
/// </summary>
public static class EventIndexMatcher
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyCollection<string>> KeyCache = new();

    /// <summary>The <see cref="EventIndexKeyAttribute"/> property names declared on <paramref name="eventType"/>.</summary>
    public static IReadOnlyCollection<string> IndexKeyNames(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return KeyCache.GetOrAdd(eventType, static type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetCustomAttribute<EventIndexKeyAttribute>() is not null)
                .Select(p => p.Name)
                .ToArray());
    }
}
