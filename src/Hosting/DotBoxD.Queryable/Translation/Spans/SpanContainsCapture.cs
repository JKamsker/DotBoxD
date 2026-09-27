using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace DotBoxD.Queryable.Translation;

internal static class SpanContainsCapture
{
    public static bool IsSupportedOverload(MethodCallExpression call)
        => call.Arguments.Count == 3 && call.Method.DeclaringType == typeof(MemoryExtensions) &&
           call.Method.IsGenericMethod && call.Method.GetGenericArguments().Length == 1 &&
           call.Method.GetParameters()[2].ParameterType ==
               typeof(IEqualityComparer<>).MakeGenericType(call.Method.GetGenericArguments()[0]);

    public static IEnumerable Capture(MethodCallExpression call, ParameterExpression parameter)
    {
        var elementType = call.Method.GetGenericArguments()[0];
        // Broad reference types can mix boxed numbers or enum types that CLR equality keeps distinct.
        if (!elementType.IsValueType && elementType != typeof(string))
        {
            throw QueryTranslationException.Unsupported(call,
                "span Contains requires a scalar value type or string element type; use a typed scalar collection.");
        }

        if (MemberPathReader.ReferencesParameter(call.Arguments[2], parameter))
        {
            throw QueryTranslationException.Unsupported(call, "the Contains comparer must be constant.");
        }

        var snapshot = typeof(SpanContainsCapture).GetMethod(nameof(Snapshot), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType);
        var capture = Expression.Call(snapshot, call.Arguments[0], call.Arguments[2]);
        if (!QueryValueFactory.TryEvaluateObject(capture, parameter, out var values))
        {
            throw QueryTranslationException.Unsupported(call, "the Contains span must be a constant collection.");
        }

        if (values is not Array array)
        {
            throw QueryTranslationException.Unsupported(call,
                "span Contains supports only a null/default equality comparer or StringComparer.Ordinal.");
        }

        return array;
    }

    // Evaluate the span and comparer in argument order before copying. A comparer getter may mutate
    // the backing array; copying the first argument early would capture different membership values.
    private static Array? Snapshot<T>(ReadOnlySpan<T> source, IEqualityComparer<T>? comparer)
        => comparer is null || ReferenceEquals(comparer, EqualityComparer<T>.Default) ||
           typeof(T) == typeof(string) && ReferenceEquals(comparer, StringComparer.Ordinal)
            ? source.ToArray()
            : null;
}
