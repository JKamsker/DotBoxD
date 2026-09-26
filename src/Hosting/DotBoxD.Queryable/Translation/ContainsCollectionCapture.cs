using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

internal static class ContainsCollectionCapture
{
    public static IReadOnlyList<QueryValue> Capture(object collection, MethodCallExpression call, Expression operand)
    {
        var itemType = call.Method.GetParameters()[call.Object is null ? 1 : 0].ParameterType;
        var capture = typeof(ContainsCollectionCapture).GetMethod(nameof(CaptureTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(itemType)
            .CreateDelegate<Func<object, MethodCallExpression, Expression, IReadOnlyList<QueryValue>>>();
        try
        {
            return capture(collection, call, operand);
        }
        catch (Exception error) when (error is not QueryTranslationException and not OperationCanceledException)
        {
            throw new QueryTranslationException(
                $"Could not capture Contains membership from the constant collection operand '{operand}'; use ToArray() when enumeration membership is intended.", error);
        }
    }

    private static IReadOnlyList<QueryValue> CaptureTyped<T>(object collection, MethodCallExpression call, Expression operand)
    {
        var values = new List<QueryValue>();
        // Primitive arrays can expose a signed/unsigned view with a different runtime element type.
        // Generic enumeration reads the values seen by the authored Contains call before boxing.
        foreach (var item in ReadMembership<T>(collection, call))
        {
            values.Add(QueryValueFactory.ToValue(item, operand));
        }

        return values;
    }

    private static IEnumerable<T> ReadMembership<T>(object collection, MethodCallExpression call)
    {
        if (call.Method.DeclaringType == typeof(Enumerable))
        {
            return new LinqMembershipValues<T>(call).Read((IEnumerable<T>)collection);
        }

        var captured = CollectionContainsSupport.Capture(call, collection);
        ContainsMethodFilterTranslator.RejectUnsupportedContainsComparer(call, collection);
        return captured is IEnumerable<T> typed ? typed : captured.Cast<T>();
    }
}
