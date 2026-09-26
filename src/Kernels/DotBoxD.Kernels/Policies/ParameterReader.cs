using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotBoxD.Kernels.Policies;

// Disambiguate from DotBoxD.Kernels.Expression (the IR model record) which otherwise wins
// name resolution inside the DotBoxD.Kernels namespace and shadows the expression-tree type.
using LinqExpression = System.Linq.Expressions.Expression;

/// <summary>
/// Reads custom capability grant parameters into an immutable string dictionary.
///
/// For object parameter shapes the readable property set (public, instance, non-indexer,
/// public getter) and an invariant string accessor are discovered once per runtime
/// <see cref="Type"/> and cached, so building many policies that reuse the same anonymous or
/// options type pays the reflection metadata enumeration and accessor compilation a single
/// time instead of once per grant (PAL-0029). The per-grant dictionary snapshot is preserved
/// so each grant still owns an independent immutable parameter map.
/// </summary>
internal static class ParameterReader
{
    // Keyed by the concrete parameter runtime type. Each entry is the ordered set of readable
    // accessors in the same order GetProperties returned them, so the produced dictionary key
    // order and duplicate-name behavior match the original per-grant reflection path exactly.
    // Weak keys release parameter types after their values have been copied into grant snapshots.
    private static readonly ConditionalWeakTable<Type, PropertyAccessor[]> AccessorsByType = new();

    public static IReadOnlyDictionary<string, string> Read(object parameters)
    {
        if (parameters is IReadOnlyDictionary<string, string> values)
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(values, StringComparer.Ordinal));
        }

        var accessors = AccessorsByType.GetValue(parameters.GetType(), BuildAccessors);
        var dictionary = new Dictionary<string, string>(accessors.Length, StringComparer.Ordinal);
        for (var i = 0; i < accessors.Length; i++)
        {
            var accessor = accessors[i];
            // Add (not indexer) preserves the original behavior of throwing on duplicate
            // property names surfaced by GetProperties (e.g. new-hidden members).
            dictionary.Add(accessor.Name, accessor.Read(parameters) ?? "");
        }

        return new ReadOnlyDictionary<string, string>(dictionary);
    }

    private static PropertyAccessor[] BuildAccessors(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var accessors = new List<PropertyAccessor>(properties.Length);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            accessors.Add(new PropertyAccessor(property.Name, CompileReader(property)));
        }

        return accessors.ToArray();
    }

    // Compile the getter once and preserve invariant conversion. Typed value formatters avoid
    // boxing allocations that survive the object-based Convert.ToString path.
    private static Func<object, string?> CompileReader(PropertyInfo property)
    {
        var instance = LinqExpression.Parameter(typeof(object), "instance");
        var typedInstance = LinqExpression.Convert(instance, property.DeclaringType!);
        var propertyAccess = LinqExpression.Property(typedInstance, property);
        var body = InvariantStringConversion(propertyAccess);
        return LinqExpression.Lambda<Func<object, string?>>(body, instance).Compile();
    }

    private static LinqExpression InvariantStringConversion(LinqExpression value)
    {
        if (value.Type == typeof(decimal) || value.Type == typeof(decimal?))
        {
            var formatter = typeof(ParameterReader).GetMethod(
                nameof(FormatDecimal), BindingFlags.Static | BindingFlags.NonPublic)!;
            return LinqExpression.Call(formatter, LinqExpression.Convert(value, typeof(decimal?)));
        }

        var valueType = Nullable.GetUnderlyingType(value.Type) ?? value.Type;
        if (valueType.IsValueType && typeof(IFormattable).IsAssignableFrom(valueType)
            && !typeof(IConvertible).IsAssignableFrom(valueType))
        {
            // Convert.ToString gives IConvertible precedence over IFormattable.
            var formatter = typeof(ParameterReader).GetMethod(
                nameof(FormatFormattable), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(valueType);
            return LinqExpression.Call(formatter, LinqExpression.Convert(value, typeof(Nullable<>).MakeGenericType(valueType)));
        }

        var boxedValue = LinqExpression.Convert(value, typeof(object));
        var convertToString = typeof(Convert).GetMethod(
            nameof(Convert.ToString),
            [typeof(object), typeof(IFormatProvider)])!;
        var invariant = LinqExpression.Constant(CultureInfo.InvariantCulture, typeof(IFormatProvider));
        return LinqExpression.Call(convertToString, boxedValue, invariant);
    }

    private static string? FormatDecimal(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? FormatFormattable<T>(T? value) where T : struct, IFormattable
        => value?.ToString(null, CultureInfo.InvariantCulture);

    private readonly record struct PropertyAccessor(string Name, Func<object, string?> Read);
}
